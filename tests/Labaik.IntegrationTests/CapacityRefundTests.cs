using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Domain.Vouchers;
using Labaik.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Labaik.IntegrationTests;

[Trait("Category", "Docker")]
public sealed class CapacityRefundTests(PaymentDatabase database) : IClassFixture<PaymentDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task Two_hundred_concurrent_verified_payments_allocate_fifty_and_refund_overflow_once()
    {
        await using var system = new PaymentTestSystem(database);
        for (var i = 0; i < 200; i++)
        {
            var seed = await database.SeedAsync(); var checkout = await system.Checkout(seed.BookingId, seed.OwnerId, "capacity");
            await system.Pay(checkout.CheckoutId!.Value);
            using var received = await system.Send(await system.Event(checkout.CheckoutId.Value, "Succeeded"));
        }
        // 200 independent requests compete for the same date, using real PostgreSQL and separate scopes.
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => system.Pump(WorkKind.Inbox)));
        await using (var db = database.App())
        {
            Assert.Equal(200, await db.CapturedPayments.CountAsync());
            Assert.Equal(50, await db.Bookings.CountAsync(x => x.State == BookingState.Paid));
            Assert.Equal(150, await db.Bookings.CountAsync(x => x.State == BookingState.Cancelled));
            Assert.Equal(50, (await db.DailyCapacities.SingleAsync()).Allocated);
            Assert.Equal(150, await db.RefundRequests.CountAsync());
        }
        await Task.WhenAll(Enumerable.Range(0, 150).Select(_ => system.Pump(WorkKind.Refund)));
        await using var after = database.App(); await using var gateway = database.Gateway();
        Assert.Equal(150, await gateway.Refunds.CountAsync()); Assert.Equal(150, await after.Bookings.CountAsync(x => x.State == BookingState.Refunded));
        Assert.Equal(50, (await after.DailyCapacities.SingleAsync()).Allocated);
    }
    [Fact]
    public async Task Lost_refund_response_reuses_key_and_duplicate_cancellation_releases_capacity_once()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var attempt = await system.Checkout(seed.BookingId, seed.OwnerId, "cancel"); await system.Pay(attempt.CheckoutId!.Value);
        await system.Pump(WorkKind.RecoverCheckout);
        async Task Cancel() { await using var scope = system.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BookingMoney>().CancelAsync(seed.BookingId, seed.OwnerId); }
        await Task.WhenAll(Cancel(), Cancel());
        system.Faults.FailRefundResponse = true; await system.Pump(WorkKind.Refund);
        await using (var db = database.App()) { Assert.Equal(0, (await db.DailyCapacities.SingleAsync()).Allocated); Assert.Equal("Pending", (await db.RefundRequests.SingleAsync()).Status); }
        system.Faults.FailRefundResponse = false; await database.DueAsync(WorkKind.Refund); await system.Pump(WorkKind.Refund);
        await using var after = database.App(); await using var gateway = database.Gateway();
        Assert.Equal(1, await gateway.Refunds.CountAsync()); Assert.Equal(BookingState.Refunded, (await after.Bookings.SingleAsync()).State);
    }
    [Fact]
    public async Task Cancellation_vs_capture_never_leaves_an_allocation_or_untracked_charge()
    {
        await using var system = new PaymentTestSystem(database); var seed = await database.SeedAsync();
        var attempt = await system.Checkout(seed.BookingId, seed.OwnerId, "race"); await system.Pay(attempt.CheckoutId!.Value);
        using var received = await system.Send(await system.Event(attempt.CheckoutId.Value, "Succeeded"));
        async Task Cancel() { await using var scope = system.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BookingMoney>().CancelAsync(seed.BookingId, seed.OwnerId); }
        await Task.WhenAll(Cancel(), system.Pump(WorkKind.Inbox)); await system.Pump(WorkKind.Refund);
        await using var db = database.App(); Assert.False((await db.Bookings.SingleAsync()).CapacityAllocated);
        Assert.Equal(BookingState.Refunded, (await db.Bookings.SingleAsync()).State); Assert.Equal(1, await db.CapturedPayments.CountAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_hundred_voucher_reservations_obey_global_and_per_user_limits(bool sameUser)
    {
        await using var system = new PaymentTestSystem(database); var now = DateTimeOffset.UtcNow;
        var voucher = new Voucher(Guid.NewGuid(), "V"+Guid.NewGuid().ToString("N"), VoucherKind.Fixed, 100, null, 20, 2, now.AddDays(1));
        var commonUser = new User(Guid.NewGuid(), "voucher-tests", Guid.NewGuid().ToString("N"), ActorRole.Customer, now);
        await using (var db = database.App()) { db.AddRange(voucher, commonUser); await db.SaveChangesAsync(); }
        var bookings = new List<(Guid Id, Guid Owner)>();
        for (var i = 0; i < 200; i++)
        {
            var user = sameUser ? commonUser : new User(Guid.NewGuid(), "voucher-tests", Guid.NewGuid().ToString("N"), ActorRole.Customer, now);
            await using var db = database.App(); if (!sameUser) { db.Users.Add(user); await db.SaveChangesAsync(); }
            var service = new BookingService(db, new BookingPolicy()); var booking = await service.CreateAsync(user.Id, new DateOnly(2030,1,1), voucher.CodeNormalized); bookings.Add((booking.Id, user.Id));
        }
        var results = await Task.WhenAll(bookings.Select(async b => { try { await system.Checkout(b.Id,b.Owner,"voucher"); return true; } catch (Application.Payments.PaymentConflictException) { return false; } }));
        Assert.Equal(sameUser ? 2 : 20, results.Count(x => x));
        await using var check = database.App(); Assert.Equal(sameUser ? 2 : 20, await check.VoucherRedemptions.CountAsync(x => x.Status == RedemptionStatus.Reserved));
    }
}
