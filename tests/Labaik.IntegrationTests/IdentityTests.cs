using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Labaik.Application.Identity;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Identity;
using Labaik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class FakeFirebaseVerifier : IFirebaseTokenVerifier
{
    public Task<VerifiedFirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (idToken is "expired" or "revoked" or "invalid-signature") throw new InvalidFirebaseTokenException();
        return Task.FromResult(new VerifiedFirebaseIdentity(idToken == "wrong-project" ? "other-project" : "test-project",
            idToken, "same-admin@example.invalid"));
    }
}

public sealed class IdentityTests(CoreDatabase database) : IClassFixture<CoreDatabase>
{
    private readonly string _signing = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly string _recovery = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private WebApplicationFactory<ApiProgram> Factory(int ipLimit = 60, int accountLimit = 30) =>
        new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Firebase:ProjectId", "test-project");
            builder.UseSetting("Auth:Issuer", "assignment"); builder.UseSetting("Auth:Audience", "assignment-clients");
            builder.UseSetting("Auth:SigningKey", _signing); builder.UseSetting("Auth:RecoveryKey", _recovery);
            builder.UseSetting("Auth:IpLimit", ipLimit.ToString()); builder.UseSetting("Auth:AccountLimit", accountLimit.ToString());
            builder.ConfigureServices(services => { services.RemoveAll<IFirebaseTokenVerifier>(); services.AddSingleton<IFirebaseTokenVerifier, FakeFirebaseVerifier>(); });
        });
    private static async Task<AuthTokens> Login(HttpClient client, string token)
    {
        using var result = await client.PostAsJsonAsync("/auth/firebase", new { idToken = token, role = "Admin", email = "same-admin@example.invalid" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("no-store", result.Headers.CacheControl!.ToString());
        return (await result.Content.ReadFromJsonAsync<AuthTokens>())!;
    }
    private static Task<HttpResponseMessage> Refresh(HttpClient client, string parent, string key) =>
        client.PostAsJsonAsync("/auth/refresh", new { refreshToken = parent, requestKey = key });

    [Fact]
    [Trait("Category", "Docker")]
    public async Task Concurrent_same_key_returns_identical_successor_and_survives_restart()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var original = await Login(client, Guid.NewGuid().ToString("N")); var key = Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Refresh(client, original.RefreshToken, key)));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var tokens = await Task.WhenAll(responses.Select(async x => (await x.Content.ReadFromJsonAsync<AuthTokens>())!));
        Assert.Single(tokens.Select(x => x.RefreshToken).Distinct());
        Assert.Single(tokens.Select(x => x.AccessToken).Distinct());
        await using var db = database.CreateContext();
        var parent = await db.RefreshTokens.SingleAsync(x => x.Hash == TokenCodec.Hash(original.RefreshToken));
        Assert.Single(await db.RefreshTokens.Where(x => x.ParentId == parent.Id).ToListAsync());
        Assert.NotEqual(original.RefreshToken, parent.Hash);
        Assert.DoesNotContain(tokens[0].RefreshToken, parent.EncryptedSuccessor!);
        await using var secondHost = Factory(); using var restarted = secondHost.CreateClient();
        using var recovered = await Refresh(restarted, original.RefreshToken, key);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(tokens[0], await recovered.Content.ReadFromJsonAsync<AuthTokens>());
        foreach (var response in responses) response.Dispose();
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task Recovery_cannot_resurrect_an_already_consumed_child()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var parent = await Login(client, Guid.NewGuid().ToString("N")); var parentKey = Guid.NewGuid().ToString("N");
        using var a = await Refresh(client, parent.RefreshToken, parentKey);
        var child = (await a.Content.ReadFromJsonAsync<AuthTokens>())!;
        using var b = await Refresh(client, child.RefreshToken, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        var grandchild = (await b.Content.ReadFromJsonAsync<AuthTokens>())!;
        using var recovery = await Refresh(client, parent.RefreshToken, parentKey);
        Assert.Equal(HttpStatusCode.Unauthorized, recovery.StatusCode);
        using var next = await Refresh(client, grandchild.RefreshToken, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task Explicit_verified_UID_role_change_revokes_old_refresh_families()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var uid = Guid.NewGuid().ToString("N"); var original = await Login(client, uid);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<VerifiedUidProvisioner>().ProvisionAsync(uid, ActorRole.Performer);
        using var old = await Refresh(client, original.RefreshToken, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        var fresh = await Login(client, uid);
        Assert.Equal("Performer", new JsonWebTokenHandler().ReadJsonWebToken(fresh.AccessToken).GetClaim("role").Value);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Docker")]
    public async Task Changed_key_or_after_grace_replay_commits_family_revocation(bool afterGrace)
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var parent = await Login(client, Guid.NewGuid().ToString("N")); var key = Guid.NewGuid().ToString("N");
        using var first = await Refresh(client, parent.RefreshToken, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var child = (await first.Content.ReadFromJsonAsync<AuthTokens>())!;
        if (afterGrace)
        {
            await using var age = database.CreateContext();
            // Move the DB deadline, not the process clock: fast, deterministic grace-boundary test.
            await age.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE app."RefreshTokens" SET "ConsumedAt"=clock_timestamp()-interval '16 seconds',
                "RetryUntil"=clock_timestamp()-interval '1 second' WHERE "Hash"={TokenCodec.Hash(parent.RefreshToken)}
                """);
        }
        using var replay = await Refresh(client, parent.RefreshToken, afterGrace ? key : Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var childAttempt = await Refresh(client, child.RefreshToken, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Unauthorized, childAttempt.StatusCode);
        await using var check = database.CreateContext();
        var row = await check.RefreshTokens.SingleAsync(x => x.Hash == TokenCodec.Hash(parent.RefreshToken));
        Assert.NotNull((await check.RefreshFamilies.SingleAsync(x => x.Id == row.FamilyId)).RevokedAt);
        Assert.Null(row.EncryptedSuccessor);
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task Matching_Admin_email_never_links_UIDs_or_roles()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var staffUid = "admin-" + Guid.NewGuid().ToString("N");
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<VerifiedUidProvisioner>().ProvisionAsync(staffUid, ActorRole.Admin);
        var staff = await Login(client, staffUid);
        var customerUid = "customer-" + Guid.NewGuid().ToString("N");
        var customer = await Login(client, customerUid);
        var decoder = new JsonWebTokenHandler();
        Assert.Equal("Admin", decoder.ReadJsonWebToken(staff.AccessToken).GetClaim("role").Value);
        Assert.Equal("Customer", decoder.ReadJsonWebToken(customer.AccessToken).GetClaim("role").Value);
        Assert.NotEqual(decoder.ReadJsonWebToken(staff.AccessToken).Subject, decoder.ReadJsonWebToken(customer.AccessToken).Subject);
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Users.CountAsync(x => x.FirebaseUid == staffUid || x.FirebaseUid == customerUid));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/auth/provision", new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/bookings")).StatusCode);
    }
    [Theory]
    [InlineData("wrong-project")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("invalid-signature")]
    [Trait("Category", "Docker")]
    public async Task Invalid_Firebase_verification_never_creates_a_local_session(string token)
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        using var result = await client.PostAsJsonAsync("/auth/firebase", new { idToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Users.AnyAsync(x => x.FirebaseUid == token));
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task JWT_has_ten_minute_lifetime_and_validated_signature_issuer_audience_and_expiry()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var token = await Login(client, Guid.NewGuid().ToString("N"));
        var parsed = new JsonWebTokenHandler().ReadJsonWebToken(token.AccessToken);
        Assert.Equal(TimeSpan.FromMinutes(10), parsed.ValidTo - parsed.IssuedAt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/me")).StatusCode);
        var sigStart = token.AccessToken.LastIndexOf('.') + 1;
        var tampered = token.AccessToken[..sigStart] + (token.AccessToken[sigStart] == 'A' ? 'B' : 'A') + token.AccessToken[(sigStart+1)..];
        client.DefaultRequestHeaders.Authorization = new("Bearer", tampered);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        var user = new User(Guid.Parse(parsed.Subject), "test-project", "expired-jwt", ActorRole.Customer, DateTimeOffset.UtcNow);
        var expired = factory.Services.GetRequiredService<TokenCodec>().Issue(user, "unused", DateTimeOffset.UtcNow.AddMinutes(-11));
        client.DefaultRequestHeaders.Authorization = new("Bearer", expired.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        var settings = factory.Services.GetRequiredService<AuthSettings>();
        var validation = settings.ValidationParameters(); validation.ValidAudience = "wrong-audience";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, validation)).IsValid);
        validation = settings.ValidationParameters(); validation.ValidIssuer = "wrong-issuer";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, validation)).IsValid);
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task IP_budget_is_atomic_across_concurrent_requests()
    {
        await using var factory = Factory(ipLimit: 3, accountLimit: 100); using var client = factory.CreateClient();
        var uid = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.PostAsJsonAsync("/auth/firebase", new { idToken = uid })));
        Assert.Equal(3, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, results.Count(x => x.StatusCode == HttpStatusCode.TooManyRequests));
        await using var db = database.CreateContext(); Assert.Equal(1, await db.Users.CountAsync(x => x.FirebaseUid == uid));
        foreach (var result in results) result.Dispose();
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task Account_limit_is_shared_across_IPs_and_unverified_email_cannot_lock_a_victim()
    {
        await using var factory = Factory(ipLimit: 60, accountLimit: 1); _ = factory.CreateClient();
        async Task<AuthResult> LoginAs(string uid, string ip)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IdentityService>().LoginAsync(uid, ip);
        }
        Assert.Equal(AuthError.InvalidCredentials, (await LoginAs("expired", "ip-one")).Error);
        var uid = Guid.NewGuid().ToString("N");
        Assert.NotNull((await LoginAs(uid, "ip-two")).Tokens);
        Assert.Equal(AuthError.RateLimited, (await LoginAs(uid, "ip-three")).Error);
        Assert.NotNull((await LoginAs(Guid.NewGuid().ToString("N"), "ip-four")).Tokens);
    }
}
