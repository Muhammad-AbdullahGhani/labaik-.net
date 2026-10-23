using Labaik.Domain.Bookings;

namespace Labaik.Domain.Vouchers;

public sealed class VoucherRedemption
{
    public Guid Id { get; private set; }
    public Guid VoucherId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BookingId { get; private set; }
    public RedemptionStatus Status { get; private set; } = RedemptionStatus.Reserved;
    public DateTimeOffset ReservedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    private VoucherRedemption() { }
    public VoucherRedemption(Guid id, Booking booking, DateTimeOffset reservedAt)
    {
        if (id == Guid.Empty || booking.VoucherId is null) throw new ArgumentException("A voucher booking is required.");
        Id = id; BookingId = booking.Id; VoucherId = booking.VoucherId.Value; UserId = booking.CustomerId;
        ReservedAt = reservedAt.ToUniversalTime();
    }
}
