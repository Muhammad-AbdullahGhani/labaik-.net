using Labaik.Domain.Pricing;

namespace Labaik.Domain.Vouchers;

public enum VoucherKind { Percentage, Fixed }
public enum RedemptionStatus { Reserved, Consumed, Released }
public readonly record struct VoucherUsage(int ActiveUses, int ActiveUserUses);

public sealed class Voucher
{
    public Guid Id { get; private set; }
    public string CodeNormalized { get; private set; } = null!;
    public VoucherKind Kind { get; private set; }
    public long? FixedMinor { get; private set; }
    public int? BasisPoints { get; private set; }
    public string Currency { get; private set; } = "SAR";
    public int MaxUses { get; private set; }
    public int PerUserLimit { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool Enabled { get; private set; }
    private Voucher() { } // EF materialization only.
    public Voucher(Guid id, string code, VoucherKind kind, long? fixedMinor, int? basisPoints,
        int maxUses, int perUserLimit, DateTimeOffset expiresAt, bool enabled = true)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (id == Guid.Empty || normalized.Length is < 1 or > 64 ||
            normalized.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Invalid voucher identifier/code.");
        if (kind == VoucherKind.Percentage ? fixedMinor is not null || basisPoints is null or < 0 or > 10000
            : kind != VoucherKind.Fixed || fixedMinor is null or < 0 || basisPoints is not null)
            throw new ArgumentException("Invalid discount shape.");
        if (maxUses <= 0 || perUserLimit <= 0) throw new ArgumentOutOfRangeException(nameof(maxUses));
        Id = id; CodeNormalized = normalized; Kind = kind; FixedMinor = fixedMinor; BasisPoints = basisPoints;
        MaxUses = maxUses; PerUserLimit = perUserLimit; ExpiresAt = expiresAt.ToUniversalTime(); Enabled = enabled;
    }
    public RuleResult CheckEligibility(DateTimeOffset now, VoucherUsage usage)
    {
        if (usage.ActiveUses < 0 || usage.ActiveUserUses < 0 || usage.ActiveUserUses > usage.ActiveUses)
            throw new ArgumentException("Invalid usage snapshot.", nameof(usage));
        if (!Enabled) return RuleResult.Deny("Voucher is disabled.");
        if (now >= ExpiresAt) return RuleResult.Deny("Voucher has expired.");
        if (usage.ActiveUses >= MaxUses) return RuleResult.Deny("Voucher maximum uses reached.");
        if (usage.ActiveUserUses >= PerUserLimit) return RuleResult.Deny("Voucher per-user limit reached.");
        return RuleResult.Allow();
    }
    public Money DiscountFor(Money subtotal)
    {
        if (Kind == VoucherKind.Fixed) return new(Math.Min(subtotal.Minor, FixedMinor!.Value));
        var bp = BasisPoints!.Value;
        // Exact floor for nonnegative values; split before multiplying to avoid long overflow at large subtotals.
        return new(checked(subtotal.Minor / 10000 * bp + subtotal.Minor % 10000 * bp / 10000));
    }
}
