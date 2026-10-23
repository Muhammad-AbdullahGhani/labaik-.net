using Labaik.Domain.Pricing;

namespace Labaik.Domain.Bookings;

public sealed class Booking
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateOnly ServiceDate { get; private set; }
    public BookingState State { get; private set; } = BookingState.Draft;
    public long Version { get; private set; } = 1;
    public long BaseMinor { get; private set; }
    public long AddonMinor { get; private set; }
    public long DiscountMinor { get; private set; }
    public long TotalMinor { get; private set; }
    public string Currency { get; private set; } = "SAR";
    public Guid? VoucherId { get; private set; }
    public bool CapacityAllocated { get; private set; }
    public DateTimeOffset? AllocationReleasedAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private Booking() { }
    public Booking(Guid id, Guid customerId, DateOnly serviceDate, PriceQuote quote, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || customerId == Guid.Empty) throw new ArgumentException("Invalid booking/customer ID.");
        if (quote.Total.Minor == 0) throw new ArgumentException("Checkout total must be greater than zero.");
        Id = id; CustomerId = customerId; ServiceDate = serviceDate; CreatedAt = createdAt.ToUniversalTime();
        BaseMinor = quote.BaseFee.Minor; AddonMinor = quote.Addon.Minor; DiscountMinor = quote.Discount.Minor;
        TotalMinor = quote.Total.Minor; VoucherId = quote.VoucherId;
    }
    public RuleResult TryTransition(BookingState target, ActorRole role)
    {
        var result = BookingStateMachine.Evaluate(State, target, role);
        if (!result.IsSuccess) return result;
        if (target == BookingState.PendingPayment && TotalMinor <= 0)
            return RuleResult.Deny("Checkout total must be greater than zero.");
        State = target;
        return result;
    }
}

public sealed class DailyCapacity
{
    public DateOnly ServiceDate { get; private set; }
    public int Limit { get; private set; }
    public int Allocated { get; private set; }
    private DailyCapacity() { }
    public DailyCapacity(DateOnly serviceDate, int limit = 50)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        ServiceDate = serviceDate; Limit = limit;
    }
}
