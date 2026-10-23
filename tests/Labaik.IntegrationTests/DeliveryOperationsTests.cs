using System.Collections.Concurrent;
using System.Text.Json;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Labaik.IntegrationTests;

[Trait("Category", "Docker")]
public sealed class DeliveryOperationsTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync()=>database.ResetAsync();
    public Task DisposeAsync()=>Task.CompletedTask;
    private sealed class Transport : INotificationTransport
    {
        public readonly ConcurrentBag<(Guid Id,NotificationText Text)> Emails=new();
        public bool InvalidPush;
        public Task EmailAsync(string address,NotificationText text,Guid id,CancellationToken ct) { Emails.Add((id,text)); return Task.CompletedTask; }
        public Task PushAsync(string token,NotificationText text,CancellationToken ct)=>InvalidPush?Task.FromException(new InvalidDeviceTokenException()):Task.CompletedTask;
    }
    [Fact]
    public async Task Repeated_paid_publication_creates_one_assignment_and_one_recipient_delivery_and_invalid_token_is_removed()
    {
        var seed=await database.SeedAsync(); var transport=new Transport {InvalidPush=true};
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {[$"Notifications:EmailRecipients:{seed.OwnerId:D}"]="recipient@example.invalid"}).Build();
        await using(var db=database.App()) {db.DeviceTokens.Add(new DeviceToken {Id=Guid.NewGuid(),UserId=seed.OwnerId,Token="test-only-device"}); db.Entry(await db.Users.SingleAsync()).Property(x=>x.Locale).CurrentValue="ar"; await db.SaveChangesAsync();}
        var source=new PaymentEvent(Guid.NewGuid(),seed.BookingId,"BookingPaid","{}");
        async Task Route() {await using var db=database.App();await new BookingEventPublisher(db,new WorkQueue(db,new WorkPolicy()),transport,config).PublishAsync(source,default);}
        await Task.WhenAll(Route(),Route());
        await using(var db=database.App())
        {
            Assert.Equal(1,await db.JobRuns.CountAsync(x=>x.Kind==WorkKind.Assign));
            var messages=await db.OutboxMessages.ToListAsync(); Assert.Equal(2,messages.Count);
            var publisher=new BookingEventPublisher(db,new WorkQueue(db,new WorkPolicy()),transport,config);
            foreach(var item in messages) await publisher.PublishAsync(new(item.Id,item.BookingId,item.Type,item.Payload),default);
            Assert.NotNull((await db.DeviceTokens.SingleAsync()).InvalidatedAt);
            Assert.Contains("تم",Assert.Single(transport.Emails).Text.Subject);
        }
    }
    [Fact]
    public async Task Old_paid_and_expired_offer_events_never_notify_a_new_offer_or_cancelled_customer()
    {
        var seed=await database.SeedAsync(); await using var db=database.App();
        var performer=new User(Guid.NewGuid(),"notifications",Guid.NewGuid().ToString("N"),ActorRole.Performer,DateTimeOffset.UtcNow);db.Users.Add(performer);
        var booking=await db.Bookings.SingleAsync();Assert.True(booking.TryTransition(BookingState.PendingPayment,ActorRole.Customer).IsSuccess);Assert.True(booking.Cancel("test",ActorRole.Customer).IsSuccess);
        db.DeviceTokens.Add(new DeviceToken {Id=Guid.NewGuid(),UserId=seed.OwnerId,Token="customer-test-device"});
        db.DeviceTokens.Add(new DeviceToken {Id=Guid.NewGuid(),UserId=performer.Id,Token="performer-test-device"});
        var old=new PerformerOffer {Id=Guid.NewGuid(),BookingId=booking.Id,PerformerId=performer.Id,Status="Expired",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)};db.PerformerOffers.Add(old);await db.SaveChangesAsync();
        var publisher=new BookingEventPublisher(db,new WorkQueue(db,new WorkPolicy()),new Transport(),new ConfigurationBuilder().Build());
        await publisher.PublishAsync(new(Guid.NewGuid(),booking.Id,"BookingPaid","{}"),default);
        await publisher.PublishAsync(new(Guid.NewGuid(),booking.Id,"OfferCreated",JsonSerializer.Serialize(new{OfferId=old.Id})),default);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }
    [Fact]
    public async Task Nightly_schedule_is_unique_across_instances_and_report_exposes_lost_capture_without_fabricating_money()
    {
        await using var system=new PaymentTestSystem(database); var seed=await database.SeedAsync();
        var attempt=await system.Checkout(seed.BookingId,seed.OwnerId,"reconcile"); await system.Pay(attempt.CheckoutId!.Value);
        async Task Tick() {await using var db=database.App(); await new OperationsScheduler(db,new WorkQueue(db,new WorkPolicy()),new WorkerIdentity()).TickAsync(default);}
        await Task.WhenAll(Tick(),Tick()); await system.Pump(WorkKind.Reconcile);
        await using(var db=database.App())
        {
            var run=Assert.Single(await db.JobRuns.Where(x=>x.Kind==WorkKind.Reconcile).ToListAsync()); Assert.Equal(WorkStatus.Completed,run.Status);
            Assert.Contains("MissingLocalCapture",run.ResultJson!); Assert.Empty(await db.CapturedPayments.ToListAsync());
            Assert.Equal(2,await db.JobRuns.CountAsync(x=>x.Kind==WorkKind.Heartbeat));
        }
        await system.Pump(WorkKind.RecoverCheckout);
        await using(var db=database.App()) {WorkQueue.Enqueue(db,WorkKind.Reconcile,Guid.NewGuid(),DateTimeOffset.UnixEpoch); await db.SaveChangesAsync();}
        await system.Pump(WorkKind.Reconcile);
        await using(var db=database.App())
        {
            var clean=await db.JobRuns.SingleAsync(x=>x.Kind==WorkKind.Reconcile && x.JobName!="nightly-reconciliation");
            using var report=JsonDocument.Parse(clean.ResultJson!); Assert.Equal(0,report.RootElement.GetProperty("MismatchCount").GetInt32());
        }
    }
    [Fact]
    public async Task Readiness_rejects_this_workers_stale_database_heartbeat_even_if_another_worker_is_alive()
    {
        await using var db=database.App();var own=new WorkerIdentity();var other=new WorkerIdentity();var queue=new WorkQueue(db,new WorkPolicy());
        await new OperationsScheduler(db,queue,own).TickAsync(default);await new OperationsScheduler(db,queue,other).TickAsync(default);
        var readiness=new WorkerReadiness(db,own);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy,(await readiness.CheckHealthAsync(new())).Status);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"JobRuns\" SET \"AvailableAt\"=clock_timestamp()-interval '31 seconds' WHERE \"JobName\"={own.Name}");
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,(await readiness.CheckHealthAsync(new())).Status);
    }
    [Fact]
    public async Task Templates_cover_both_locales_and_never_include_private_payment_or_file_details()
    {
        await Task.CompletedTask;
        foreach(var type in new[]{"BookingPaid","OfferCreated","BookingCompleted","RefundRequired","AccountantAlert","PaymentMismatch"})
        {
            var english=NotificationTemplates.Render(type,"en"); var arabic=NotificationTemplates.Render(type,"ar");
            Assert.NotEqual(english,arabic); Assert.DoesNotContain("http",english.Body); Assert.DoesNotContain("http",arabic.Body);
        }
    }
}
