using Labaik.Application.Payments;
using Labaik.Infrastructure.Operations;
using Labaik.Domain.Bookings;
using Labaik.Domain.Vouchers;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Payments;

public sealed class BookingPolicy
{
    public int DailyLimit { get; init; } = 50;
    public long BaseMinor { get; init; } = 10000;
    public Dictionary<DateOnly, long> DateAddons { get; init; } = new();
}
public sealed class BookingMoney(AppDbContext db, BookingPolicy policy)
{
    public async Task ReserveVoucherAsync(Booking booking, DateTimeOffset now, CancellationToken ct)
    {
        if (booking.VoucherId is null) return;
        var voucher = await LockVoucher(booking.VoucherId.Value, ct);
        var redemption = await db.VoucherRedemptions.SingleOrDefaultAsync(x => x.BookingId == booking.Id, ct);
        if (redemption?.Status is RedemptionStatus.Reserved or RedemptionStatus.Consumed) return;
        var active = db.VoucherRedemptions.Where(x => x.VoucherId == voucher.Id && x.Status != RedemptionStatus.Released);
        var result = voucher.CheckEligibility(now, new(await active.CountAsync(ct), await active.CountAsync(x => x.UserId == booking.CustomerId, ct)));
        if (!result.IsSuccess) throw new PaymentConflictException(result.Reason!);
        if (redemption is null) db.VoucherRedemptions.Add(new VoucherRedemption(Guid.NewGuid(), booking, now)); else redemption.Reserve(now);
    }
    public async Task ConsumeVoucherAsync(Booking booking, CancellationToken ct)
    {
        if (booking.VoucherId is null) return;
        _ = await LockVoucher(booking.VoucherId.Value, ct);
        (await db.VoucherRedemptions.SingleOrDefaultAsync(x => x.BookingId == booking.Id, ct))?.Consume();
    }
    public async Task ReleaseVoucherAsync(Booking booking, DateTimeOffset now, CancellationToken ct)
    {
        if (booking.VoucherId is null) return;
        _ = await LockVoucher(booking.VoucherId.Value, ct);
        (await db.VoucherRedemptions.SingleOrDefaultAsync(x => x.BookingId == booking.Id, ct))?.Release(now);
    }
    private Task<Voucher> LockVoucher(Guid id, CancellationToken ct) => db.Vouchers.FromSqlInterpolated($"SELECT * FROM app.\"Vouchers\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
    public async Task<bool> AllocateAsync(Booking booking, CancellationToken ct)
    {
        if (booking.CapacityAllocated) return true;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.\"DailyCapacities\" (\"ServiceDate\",\"Limit\",\"Allocated\") VALUES ({booking.ServiceDate},{policy.DailyLimit},0) ON CONFLICT (\"ServiceDate\") DO NOTHING", ct);
        // PostgreSQL serializes this conditional update; the CHECK is a second protection against overflow.
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"DailyCapacities\" SET \"Allocated\"=\"Allocated\"+1 WHERE \"ServiceDate\"={booking.ServiceDate} AND \"Allocated\"<\"Limit\"", ct);
        if (changed == 1) booking.AllocateCapacity();
        return changed == 1;
    }
    public async Task ReleaseCapacityAsync(Booking booking, DateTimeOffset now, CancellationToken ct)
    {
        if (!booking.CapacityAllocated) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"DailyCapacities\" SET \"Allocated\"=\"Allocated\"-1 WHERE \"ServiceDate\"={booking.ServiceDate}", ct);
        booking.ReleaseCapacity(now);
    }
    public async Task RequestRefundAsync(CapturedPayment capture, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.RefundRequests.AnyAsync(x => x.CapturedPaymentId == capture.Id, ct) || db.ChangeTracker.Entries<RefundRequest>().Any(x => x.Entity.CapturedPaymentId == capture.Id)) return;
        var refund = new RefundRequest { Id = Guid.NewGuid(), CapturedPaymentId = capture.Id, GatewayKey = $"refund:{capture.Id:N}", Reason = reason };
        db.RefundRequests.Add(refund); WorkQueue.Enqueue(db, WorkKind.Refund, refund.Id, now);
    }
    public async Task CancelAsync(Guid bookingId, Guid actorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AssignmentLock.TakeAsync(db, ct);
        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={bookingId} FOR UPDATE").SingleAsync(ct);
        var actor = await db.Users.SingleAsync(x => x.Id == actorId, ct);
        if (actor.Status != Domain.Users.UserStatus.Active) throw new PaymentConflictException("Inactive account.");
        if (booking.CustomerId != actorId && actor.Role is not (ActorRole.Supervisor or ActorRole.Admin)) throw new PaymentConflictException("Not authorized to cancel.");
        if (booking.State is BookingState.Cancelled or BookingState.Refunded) return;
        var result = booking.Cancel("CustomerOrSupervisorCancellation", actor.Role);
        if (!result.IsSuccess) throw new PaymentConflictException(result.Reason!);
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        await db.PerformerOffers.Where(x => x.BookingId == bookingId && (x.Status == "Pending" || x.Status == "Accepted")).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Cancelled"), ct);
        await ReleaseCapacityAsync(booking, now, ct); await ReleaseVoucherAsync(booking, now, ct);
        var capture = await db.CapturedPayments.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (capture is not null) await RequestRefundAsync(capture, "Cancelled", now, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
