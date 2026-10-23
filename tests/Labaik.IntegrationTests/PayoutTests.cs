using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Labaik.IntegrationTests;

[Trait("Category", "Docker")]
public sealed class PayoutTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private readonly PayoutPolicy _policy = new() { PerformerFeeMinor = 700 };
    private readonly DateOnly _week = new(2020,1,6);
    private async Task<(Guid Booking, Guid Performer, Guid Accountant, Guid Supervisor, Guid Customer)> Seed()
    {
        var seed = await database.SeedAsync(); await using var db = database.App();
        User Person(ActorRole role) => new(Guid.NewGuid(),"payout-tests",Guid.NewGuid().ToString("N"),role,DateTimeOffset.UtcNow);
        var performer = Person(ActorRole.Performer); var accountant = Person(ActorRole.Accountant); var supervisor = Person(ActorRole.Supervisor);
        db.AddRange(performer,accountant,supervisor);
        var booking = await db.Bookings.SingleAsync(x => x.Id == seed.BookingId);
        foreach (var (state, role) in new[] { (BookingState.PendingPayment, ActorRole.Customer), (BookingState.Paid, ActorRole.System), (BookingState.Offered, ActorRole.System), (BookingState.Accepted, ActorRole.Performer) }) Assert.True(booking.TryTransition(state,role).IsSuccess);
        db.PerformerOffers.Add(new PerformerOffer {Id=Guid.NewGuid(),BookingId=booking.Id,PerformerId=performer.Id,Status="Accepted",ExpiresAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync();
        // Synthetic checksum-valid account; no bank is contacted by these tests.
        var suffix="00000000000000000000"; var remainder=0;
        foreach(var digit in suffix+"281000") remainder=(remainder*10+digit-'0')%97;
        _policy.Accounts[performer.Id.ToString()]=$"SA{98-remainder:D2}{suffix}";
        return (booking.Id,performer.Id,accountant.Id,supervisor.Id,seed.OwnerId);
    }
    private CompletionService Completion(Labaik.Infrastructure.Persistence.AppDbContext db) => new(db,new WorkQueue(db,new WorkPolicy()),_policy);
    private PayoutService Payout(Labaik.Infrastructure.Persistence.AppDbContext db) => new(db,new WorkQueue(db,new WorkPolicy()),_policy);
    private async Task Complete((Guid Booking, Guid Performer, Guid Accountant, Guid Supervisor, Guid Customer) data)
    {
        await using var db=database.App(); var service=Completion(db);
        await service.MoveAsync(data.Booking,data.Performer,BookingState.InProgress);
        await Assert.ThrowsAsync<PaymentConflictException>(()=>service.MoveAsync(data.Booking,data.Performer,BookingState.ProofSubmitted));
        db.StoredFiles.Add(new StoredFile {Id=Guid.NewGuid(),BookingId=data.Booking,OwnerId=data.Performer,Kind="Proof",ObjectKey=Guid.NewGuid().ToString(),Status="Uploaded",Size=1}); await db.SaveChangesAsync();
        await service.MoveAsync(data.Booking,data.Performer,BookingState.ProofSubmitted);
        await Assert.ThrowsAsync<PaymentConflictException>(()=>service.MoveAsync(data.Booking,data.Supervisor,BookingState.Completed));
        var file=await db.StoredFiles.SingleAsync(); file.Status="Approved"; await db.SaveChangesAsync();
        await service.MoveAsync(data.Booking,data.Supervisor,BookingState.Completed);
        await service.MoveAsync(data.Booking,data.Supervisor,BookingState.Completed);
        var earning=await db.Earnings.SingleAsync(); earning.CompletedAt=new DateTimeOffset(2020,1,7,0,0,0,TimeSpan.Zero); await db.SaveChangesAsync();
    }
    [Fact]
    public async Task Completion_requires_assigned_performer_and_approved_proof_and_credits_once()
    {
        var data=await Seed(); await using(var db=database.App())
            await Assert.ThrowsAsync<PaymentConflictException>(()=>Completion(db).MoveAsync(data.Booking,data.Customer,BookingState.InProgress));
        await Complete(data); await using var check=database.App();
        Assert.Equal(700,(await check.Earnings.SingleAsync()).AmountMinor); Assert.Equal("Finished",(await check.PerformerOffers.SingleAsync()).Status);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_batch_creation_never_claims_an_earning_twice(bool sameKey)
    {
        var data=await Seed(); await Complete(data);
        async Task<PayoutBatch?> Create(string key) {await using var db=database.App(); return await Payout(db).CreateAsync(data.Performer,_week,key,data.Accountant);}
        var results=await Task.WhenAll(Enumerable.Range(0,20).Select(i=>Create(sameKey?"same":$"key-{i}")));
        await using var check=database.App(); Assert.Single(await check.PayoutBatches.ToListAsync()); Assert.Single(await check.PayoutItems.ToListAsync());
        Assert.Equal("Reserved",(await check.Earnings.SingleAsync()).Status);
        Assert.Single(results.Where(x=>x is not null).Select(x=>x!.Id).Distinct());
        await Assert.ThrowsAsync<PaymentConflictException>(()=>CreateWithChangedKey());
        async Task CreateWithChangedKey() {await using var db=database.App(); var batch=await db.PayoutBatches.SingleAsync(); await Payout(db).CreateAsync(data.Performer,_week.AddDays(7),batch.RequestKey,data.Accountant);}
    }
    [Fact]
    public async Task Export_is_repeatable_and_not_payment_failed_claim_can_be_retried_paid_cannot()
    {
        var data=await Seed(); await Complete(data); await using var db=database.App(); var service=Payout(db);
        var first=(await service.CreateAsync(data.Performer,_week,"first",data.Accountant))!;
        Assert.Equal(await service.ExportAsync(first.Id,data.Accountant),await service.ExportAsync(first.Id,data.Accountant)); Assert.Equal("Pending",first.Status);
        await service.ConfirmAsync(first.Id,"Failed","bank-failed",data.Accountant);
        var retry=(await service.CreateAsync(data.Performer,_week,"retry",data.Accountant))!;
        await service.ExportAsync(retry.Id,data.Accountant); await service.ConfirmAsync(retry.Id,"Paid","bank-paid",data.Accountant);
        await service.ConfirmAsync(retry.Id,"Paid","bank-paid",data.Accountant);
        await Assert.ThrowsAsync<PaymentConflictException>(()=>service.ConfirmAsync(retry.Id,"Failed","other",data.Accountant));
        Assert.Null(await service.CreateAsync(data.Performer,_week,"third",data.Accountant));
        Assert.Equal("Paid",(await db.Earnings.SingleAsync()).Status);
    }
    [Fact]
    public async Task Paid_and_failed_race_has_one_terminal_result_and_disputes_exclude_new_claims()
    {
        var data=await Seed(); await Complete(data); Guid id;
        await using(var db=database.App()) {var batch=(await Payout(db).CreateAsync(data.Performer,_week,"race",data.Accountant))!; id=batch.Id; await Payout(db).ExportAsync(id,data.Accountant);}
        async Task<bool> Confirm(string status) {await using var db=database.App();try {await Payout(db).ConfirmAsync(id,status,"race-bank",data.Accountant);return true;}catch(PaymentConflictException){return false;}}
        var results=await Task.WhenAll(Confirm("Paid"),Confirm("Failed")); Assert.Single(results,x=>x);
        await using(var db=database.App()) {var batch=await db.PayoutBatches.SingleAsync(); Assert.Equal(batch.Status=="Paid"?"Paid":"Available",(await db.Earnings.SingleAsync()).Status);}
        await using(var db=database.App()) await Completion(db).MoveAsync(data.Booking,data.Customer,BookingState.Disputed);
        await using(var db=database.App()) Assert.Null(await Payout(db).CreateAsync(data.Performer,_week,"disputed",data.Accountant));
    }
    [Fact]
    public async Task Two_weekly_workers_create_one_batch_with_a_stable_schedule_key()
    {
        var data=await Seed();await Complete(data);await using var system=new PaymentTestSystem(database);
        async Task Tick(){await using var db=database.App();await new OperationsScheduler(db,new WorkQueue(db,new WorkPolicy()),new WorkerIdentity()).TickAsync(default);}
        await Task.WhenAll(Tick(),Tick());await Task.WhenAll(system.Pump(WorkKind.PayoutSweep),system.Pump(WorkKind.PayoutSweep));
        await using var check=database.App();Assert.Single(await check.PayoutBatches.ToListAsync());Assert.Single(await check.PayoutItems.ToListAsync());
        Assert.Equal(WorkStatus.Completed,(await check.JobRuns.SingleAsync(x=>x.Kind==WorkKind.PayoutSweep)).Status);
    }
    [Fact]
    public async Task Customer_cannot_create_or_export_or_confirm_payout()
    {
        var data=await Seed(); await Complete(data); await using var db=database.App(); var service=Payout(db);
        await Assert.ThrowsAsync<PaymentConflictException>(()=>service.CreateAsync(data.Performer,_week,"no",data.Customer));
        var batch=(await service.CreateAsync(data.Performer,_week,"yes",data.Accountant))!;
        await Assert.ThrowsAsync<PaymentConflictException>(()=>service.ExportAsync(batch.Id,data.Customer));
        await Assert.ThrowsAsync<PaymentConflictException>(()=>service.ConfirmAsync(batch.Id,"Failed","no",data.Customer));
    }
}

