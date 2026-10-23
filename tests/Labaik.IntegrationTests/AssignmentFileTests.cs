using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Labaik.IntegrationTests;

[Trait("Category", "Docker")]
public sealed class AssignmentTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private async Task<(Guid Booking, Guid Performer)> Offer(PaymentTestSystem system)
    {
        var seed = await database.SeedAsync(); var performer = new User(Guid.NewGuid(), "assignment-test", Guid.NewGuid().ToString("N"), ActorRole.Performer, DateTimeOffset.UtcNow);
        await using (var db = database.App()) { db.Users.Add(performer); await db.SaveChangesAsync(); }
        var attempt = await system.Checkout(seed.BookingId, seed.OwnerId,"offer"); await system.Pay(attempt.CheckoutId!.Value); await system.Pump(WorkKind.RecoverCheckout);
        await using (var db = database.App()) { WorkQueue.Enqueue(db, WorkKind.Assign,seed.BookingId,DateTimeOffset.UtcNow); await db.SaveChangesAsync(); }
        await database.DueAsync(WorkKind.Assign); await system.Pump(WorkKind.Assign);
        await using (var check=database.App())
        {
            var job=await check.JobRuns.SingleAsync(x=>x.Kind==WorkKind.Assign && x.EntityId==seed.BookingId);
            Assert.True(await check.PerformerOffers.AnyAsync(), $"Assignment job {job.Status}, error {job.LastError}");
        }
        return (seed.BookingId,performer.Id);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accept_and_expiry_race_has_one_consistent_outcome_and_accept_is_idempotent(bool deadlinePassed)
    {
        await using var system = new PaymentTestSystem(database); var data = await Offer(system); Guid offerId;
        await using (var db = database.App())
        {
            var offer = await db.PerformerOffers.SingleAsync(); offerId = offer.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"PerformerOffers\" SET \"ExpiresAt\"=clock_timestamp()+({(deadlinePassed ? -10 : 10)}*interval '1 second') WHERE \"Id\"={offer.Id}");
        }
        await database.DueAsync(WorkKind.ExpireOffer);
        async Task<bool> Accept() { try { await using var scope=system.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<AssignmentService>().AcceptAsync(offerId,data.Performer); return true; } catch(PaymentConflictException) { return false; } }
        var accepting = Accept(); await Task.WhenAll(accepting, system.Pump(WorkKind.ExpireOffer));
        Assert.Equal(!deadlinePassed, await accepting);
        await using var check = database.App(); var saved = await check.PerformerOffers.SingleAsync(); var booking = await check.Bookings.SingleAsync();
        Assert.Equal(deadlinePassed ? "Expired" : "Accepted",saved.Status);
        Assert.Equal(deadlinePassed ? BookingState.Paid : BookingState.Accepted,booking.State);
        if (!deadlinePassed) Assert.True(await Accept());
    }
    [Fact]
    public async Task Concurrent_schedulers_never_double_assign_and_suspended_performers_are_excluded()
    {
        await using var system = new PaymentTestSystem(database); var data = await Offer(system);
        var seed = await database.SeedAsync(); var attempt = await system.Checkout(seed.BookingId,seed.OwnerId,"second"); await system.Pay(attempt.CheckoutId!.Value); await database.DueAsync(WorkKind.RecoverCheckout); await system.Drain(WorkKind.RecoverCheckout);
        await using (var db = database.App()) { WorkQueue.Enqueue(db,WorkKind.Assign,seed.BookingId,DateTimeOffset.UtcNow); await db.SaveChangesAsync(); }
        await database.DueAsync(WorkKind.Assign); await Task.WhenAll(system.Pump(WorkKind.Assign),system.Pump(WorkKind.Assign));
        await using (var db = database.App()) Assert.Equal(1,await db.PerformerOffers.CountAsync(x=>x.Status=="Pending"));
        var supervisor = new User(Guid.NewGuid(),"assignment-test",Guid.NewGuid().ToString("N"),ActorRole.Supervisor,DateTimeOffset.UtcNow);
        await using (var db=database.App()) { db.Users.Add(supervisor); await db.SaveChangesAsync(); }
        await using(var scope=system.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AssignmentService>().SuspendAsync(data.Performer,supervisor.Id);
        await using(var db=database.App()) await db.Database.ExecuteSqlRawAsync("UPDATE app.\"PerformerOffers\" SET \"ExpiresAt\"=clock_timestamp()-interval '1 second'");
        await database.DueAsync(WorkKind.ExpireOffer); await system.Pump(WorkKind.ExpireOffer); await database.DueAsync(WorkKind.Assign); await system.Drain(WorkKind.Assign);
        await using var check=database.App(); Assert.Equal(0,await check.PerformerOffers.CountAsync(x=>x.Status=="Pending"));
    }
}

[Trait("Category", "Docker")]
public sealed class PrivateFileTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync()=>database.ResetAsync();
    public Task DisposeAsync()=>Task.CompletedTask;
    [Fact]
    public async Task Private_create_only_SAS_is_exact_path_no_overwrite_and_owner_checked()
    {
        var key=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var azurite=new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
            .WithEnvironment("AZURITE_ACCOUNTS",$"labaiktest:{key}").WithPortBinding(10000,true)
            .WithCommand("azurite-blob","--blobHost","0.0.0.0","--skipApiVersionCheck","--silent")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(10000)).Build();
        await azurite.StartAsync();
        var store=new AzurePrivateBlobStore($"DefaultEndpointsProtocol=http;AccountName=labaiktest;AccountKey={key};BlobEndpoint=http://{azurite.Hostname}:{azurite.GetMappedPublicPort(10000)}/labaiktest;","private-evidence");
        var seed=await database.SeedAsync(); var supervisor=new User(Guid.NewGuid(),"files",Guid.NewGuid().ToString("N"),ActorRole.Supervisor,DateTimeOffset.UtcNow);
        var stranger=new User(Guid.NewGuid(),"files",Guid.NewGuid().ToString("N"),ActorRole.Customer,DateTimeOffset.UtcNow);
        await using var db=database.App(); db.AddRange(supervisor,stranger); await db.SaveChangesAsync();
        var files=new PrivateFileService(db,store); var upload=await files.UploadAsync(seed.BookingId,seed.OwnerId,"Passport");
        using var http=new HttpClient();
        async Task<HttpResponseMessage> Put(Uri url,string text) { using var message=new HttpRequestMessage(HttpMethod.Put,url) {Content=new StringContent(text)}; message.Content.Headers.ContentType=new MediaTypeHeaderValue("image/jpeg"); message.Headers.Add("x-ms-blob-type","BlockBlob"); return await http.SendAsync(message); }
        using var written=await Put(upload.Url,"test-image"); Assert.True(written.IsSuccessStatusCode,$"Upload status {(int)written.StatusCode}");
        var other=new UriBuilder(upload.Url) {Path=upload.Url.AbsolutePath+"-other"}; using var wrong=await Put(other.Uri,"bad"); Assert.False(wrong.IsSuccessStatusCode);
        await files.CompleteAsync(upload.FileId,seed.OwnerId); await files.ApproveAsync(upload.FileId,supervisor.Id);
        using var overwrite=await Put(upload.Url,"replacement"); Assert.False(overwrite.IsSuccessStatusCode);
        await Assert.ThrowsAsync<PaymentConflictException>(()=>files.ReadAsync(upload.FileId,stranger.Id));
        var read=await files.ReadAsync(upload.FileId,seed.OwnerId); Assert.Equal("test-image",await http.GetStringAsync(read.Url));
        var unsigned=new UriBuilder(read.Url) {Query=""}; using var denied=await http.GetAsync(unsigned.Uri); Assert.False(denied.IsSuccessStatusCode);
        using var readCannotWrite=await Put(read.Url,"bad"); Assert.False(readCannotWrite.IsSuccessStatusCode);
        Assert.True(upload.ExpiresAt<=DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
