using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Domain.Vouchers;
using Labaik.Infrastructure.Identity;
using Labaik.Infrastructure.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NBomber.CSharp;
using Xunit;

namespace Labaik.IntegrationTests;

// NBomber's process-wide runner is not safe to run concurrently with another NBomber runner.
[CollectionDefinition("Load",DisableParallelization=true)]
public sealed class LoadCollection;
[Collection("Load")]
[Trait("Category","Docker")]
[Trait("Category","Load")]
public sealed class LoadTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>,IAsyncLifetime
{
    public Task InitializeAsync()=>database.ResetAsync();
    public Task DisposeAsync()=>Task.CompletedTask;
    private WebApplicationFactory<ApiProgram> Api(PaymentTestSystem system) => new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder=>
    {
        builder.UseSetting("ConnectionStrings:Database",database.AppConnection);
        builder.UseSetting("Auth:SigningKey",Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Auth:RecoveryKey",Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Auth:Issuer","load-tests");builder.UseSetting("Auth:Audience","load-tests");builder.UseSetting("Firebase:ProjectId","load-tests");
        builder.UseSetting("Payments:WebhookEncryptionKey",Convert.ToBase64String(database.EncryptionKey));
        builder.UseSetting("Payments:WebhookSignatureKey",Convert.ToBase64String(database.SignatureKey));
        builder.UseSetting("Payments:WorkersEnabled","false");
        builder.ConfigureServices(services=>services.AddSingleton<IPaymentGateway>(new HttpPaymentGateway(system.GatewayClient)));
    });
    private static void Run(string name,Func<int,Task<bool>> operation)
    {
        var index=-1; var started=0; var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var failures=0;
        var scenario=Scenario.Create(name,async _=>
        {
            var current=Interlocked.Increment(ref index);
            if(current>=200) return Response.Ok();
            if(Interlocked.Increment(ref started)==200)gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
            try {if(await operation(current))return Response.Ok();}catch(Exception){ /* No URLs, credentials or response bodies in load reports. */ }
            Interlocked.Increment(ref failures);return Response.Fail();
        }).WithoutWarmUp().WithLoadSimulations(Simulation.IterationsForConstant(copies:200,iterations:200));
        _=NBomberRunner.RegisterScenarios(scenario).WithScenarioCompletionTimeout(TimeSpan.FromMinutes(2))
            .WithReportFolder(Path.Combine(Path.GetTempPath(),"labaik-load-reports"))
            .WithReportFileName(name).Run();
        Assert.Equal(200,started); Assert.Equal(0,failures);
    }
    [Fact]
    public async Task Two_hundred_concurrent_signed_HTTP_callbacks_never_oversell()
    {
        await using var system=new PaymentTestSystem(database); await using var api=Api(system); using var client=api.CreateClient();
        var events=new List<byte[]>();
        for(var i=0;i<200;i++)
        {
            var seed=await database.SeedAsync();var attempt=await system.Checkout(seed.BookingId,seed.OwnerId,"load"); await system.Pay(attempt.CheckoutId!.Value);
            events.Add(await system.Event(attempt.CheckoutId.Value,"Succeeded"));
        }
        await Task.Run(()=>Run("capacity-200",async i=>
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/webhooks/gateway") {Content=new ByteArrayContent(events[i])};
            request.Headers.Add("X-Gateway-Signature",system.Codec.Sign(events[i]));
            using var response=await client.SendAsync(request); if(response.StatusCode!=HttpStatusCode.Accepted)return false;
            return await system.Pump(WorkKind.Inbox);
        }));
        await using var check=database.App(); Assert.Equal(50,(await check.DailyCapacities.SingleAsync()).Allocated);
        Assert.Equal(200,await check.CapturedPayments.CountAsync()); Assert.Equal(150,await check.RefundRequests.CountAsync());
        Assert.Equal(50,await check.Bookings.CountAsync(x=>x.State==BookingState.Paid));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_hundred_authenticated_HTTP_checkouts_obey_voucher_global_and_user_limits(bool sameUser)
    {
        await using var system=new PaymentTestSystem(database);await using var api=Api(system);using var client=api.CreateClient();
        var codec=api.Services.GetRequiredService<TokenCodec>();
        var now=DateTimeOffset.UtcNow;var voucher=new Voucher(Guid.NewGuid(),"L"+Guid.NewGuid().ToString("N"),VoucherKind.Fixed,100,null,20,2,now.AddHours(1));
        var common=new User(Guid.NewGuid(),"load-tests",Guid.NewGuid().ToString("N"),ActorRole.Customer,now);
        await using(var db=database.App()){db.AddRange(voucher,common);await db.SaveChangesAsync();}
        var rows=new List<(Guid Id,string Jwt)>();
        for(var i=0;i<200;i++)
        {
            var user=sameUser?common:new User(Guid.NewGuid(),"load-tests",Guid.NewGuid().ToString("N"),ActorRole.Customer,now);
            await using var db=database.App();if(!sameUser){db.Users.Add(user);await db.SaveChangesAsync();}
            var jwt=codec.Issue(user,"unused",now).AccessToken;
            using var create=new HttpRequestMessage(HttpMethod.Post,"/bookings") {Content=JsonContent.Create(new{ServiceDate=new DateOnly(2030,1,1),VoucherCode=voucher.CodeNormalized})};
            create.Headers.Authorization=new("Bearer",jwt); using var response=await client.SendAsync(create); response.EnsureSuccessStatusCode();
            var booking=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();rows.Add((booking.GetProperty("id").GetGuid(),jwt));
        }
        var success=0;
        await Task.Run(()=>Run("voucher-200-"+(sameUser?"user":"global"),async i=>
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,$"/bookings/{rows[i].Id}/checkout") {Content=JsonContent.Create(new{RequestKey="load"})};request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",rows[i].Jwt);
            using var response=await client.SendAsync(request);if(response.StatusCode==HttpStatusCode.OK){Interlocked.Increment(ref success);return true;}
            return response.StatusCode==HttpStatusCode.Conflict;
        }));
        Assert.Equal(sameUser?2:20,success);await using var check=database.App();Assert.Equal(success,await check.VoucherRedemptions.CountAsync(x=>x.Status==RedemptionStatus.Reserved));
    }
}
