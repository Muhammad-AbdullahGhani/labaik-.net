using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Identity;
using Labaik.Infrastructure.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Labaik.IntegrationTests;

[Trait("Category","Docker")]
public sealed class DemoUiTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync()=>database.ResetAsync();
    public Task DisposeAsync()=>Task.CompletedTask;
    private WebApplicationFactory<ApiProgram> Factory(PaymentTestSystem system,string environment="Demo",bool enabled=true)
        => new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder=>
        {
            builder.UseEnvironment(environment); builder.UseSetting("Demo:Enabled",enabled.ToString());
            builder.UseSetting("ConnectionStrings:Database",database.AppConnection);
            builder.UseSetting("Payments:WorkersEnabled","false");
            builder.UseSetting("Payments:GatewayUrl","http://gateway.test/"); builder.UseSetting("Gateway:ApiKey",database.ApiKey);
            builder.UseSetting("Firebase:ProjectId","demo-project"); builder.UseSetting("Firebase:CredentialsJson","not-a-browser-value");
            builder.UseSetting("Auth:Issuer","demo-tests");builder.UseSetting("Auth:Audience","demo-tests");
            builder.UseSetting("Auth:SigningKey",Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseSetting("Auth:RecoveryKey",Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseSetting("Demo:WebApiKey","synthetic-public-web-key");builder.UseSetting("Demo:WebAppId","synthetic-app");
            builder.ConfigureServices(services=>services.AddHttpClient("DemoGateway").ConfigurePrimaryHttpMessageHandler(()=>new ForwardGateway(system.GatewayClient)));
        });
    private sealed class ForwardGateway(HttpClient gateway) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            using var copy=new HttpRequestMessage(request.Method,request.RequestUri!.PathAndQuery);
            if(request.Content is not null) copy.Content=new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(ct));
            if(copy.Content is not null) copy.Content.Headers.ContentType=new("application/json");
            return await gateway.SendAsync(copy,ct);
        }
    }
    private async Task Login(HttpClient client,WebApplicationFactory<ApiProgram> factory,Guid userId)
    {
        await using var db=database.App(); var user=await db.Users.SingleAsync(x=>x.Id==userId);
        var issued=factory.Services.GetRequiredService<TokenCodec>().Issue(user,"unused",DateTimeOffset.UtcNow);
        client.DefaultRequestHeaders.Authorization=new("Bearer",issued.AccessToken);
    }
    [Theory]
    [InlineData("Production",true)]
    [InlineData("Demo",false)]
    public async Task Demo_routes_are_absent_unless_both_explicit_gates_are_enabled(string environment,bool enabled)
    {
        await using var system=new PaymentTestSystem(database);await using var factory=Factory(system,environment,enabled);using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/demo/config.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync($"/demo/bookings/{Guid.NewGuid()}/pay",new{outcome="Succeeded"})).StatusCode);
    }
    [Fact]
    public async Task Public_UI_configuration_excludes_private_credentials_and_bookings_require_authentication()
    {
        await using var system=new PaymentTestSystem(database);await using var factory=Factory(system);using var client=factory.CreateClient();
        using var response=await client.GetAsync("/demo/config.js");var body=await response.Content.ReadAsStringAsync();
        using var page=await client.GetAsync("/demo/");Assert.Equal(HttpStatusCode.OK,page.StatusCode);Assert.Contains("Sign in to your account",await page.Content.ReadAsStringAsync());
        Assert.Contains("synthetic-public-web-key",body);Assert.DoesNotContain(database.ApiKey,body);Assert.DoesNotContain("not-a-browser-value",body);Assert.DoesNotContain("SigningKey",body);
        Assert.Equal("no-store",response.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/demo/bookings")).StatusCode);
    }
    [Fact]
    public async Task Customer_sees_own_bookings_only_and_cannot_simulate_another_customers_payment_or_read_staff_ledger()
    {
        var owner=await database.SeedAsync();var other=await database.SeedAsync();
        await using var system=new PaymentTestSystem(database);await using var factory=Factory(system);using var client=factory.CreateClient();await Login(client,factory,owner.OwnerId);
        var list=await client.GetFromJsonAsync<JsonElement[]>("/demo/bookings");Assert.Equal(owner.BookingId,Assert.Single(list!).GetProperty("id").GetGuid());
        Assert.Equal("1000",list![0].GetProperty("totalMinor").GetString());
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync($"/demo/bookings/{other.BookingId}/pay",new{outcome="Succeeded"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/demo/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/demo/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/operations")).StatusCode);
        await using var gateway=database.Gateway();Assert.Empty(await gateway.Charges.ToListAsync());
    }
    [Fact]
    public async Task Simulation_updates_fake_gateway_once_but_only_existing_worker_records_capture_and_paid_state()
    {
        var seed=await database.SeedAsync();await using var system=new PaymentTestSystem(database);
        await system.Checkout(seed.BookingId,seed.OwnerId,"ui-checkout");await using var factory=Factory(system);using var client=factory.CreateClient();await Login(client,factory,seed.OwnerId);
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.Accepted,(await client.PostAsJsonAsync($"/demo/bookings/{seed.BookingId}/pay",new{outcome="Succeeded"})).StatusCode);
        await using(var db=database.App()){Assert.Empty(await db.CapturedPayments.ToListAsync());Assert.Equal(BookingState.PendingPayment,(await db.Bookings.SingleAsync()).State);}
        await system.Pump(WorkKind.RecoverCheckout);
        await using(var db=database.App()){Assert.Single(await db.CapturedPayments.ToListAsync());Assert.Equal(BookingState.Paid,(await db.Bookings.SingleAsync()).State);}
        await using var gateway=database.Gateway();Assert.Single(await gateway.Charges.ToListAsync());
    }
    [Fact]
    public async Task Privileged_reads_check_database_role_and_disabled_accounts_cannot_use_demo_actions()
    {
        var seed=await database.SeedAsync();await using var system=new PaymentTestSystem(database);await using var factory=Factory(system);using var client=factory.CreateClient();await Login(client,factory,seed.OwnerId);
        await using(var db=database.App()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"Users\" SET \"Role\"='Admin' WHERE \"Id\"={seed.OwnerId}");
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/demo/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/demo/jobs")).StatusCode);
        await using(var db=database.App()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"Users\" SET \"Status\"='Disabled' WHERE \"Id\"={seed.OwnerId}");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/demo/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/demo/bookings/{seed.BookingId}/pay",new{outcome="Succeeded"})).StatusCode);
    }
}
