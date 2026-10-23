namespace Labaik.Domain.Pricing;

public sealed class PricingPolicy(Money baseFee, IReadOnlyDictionary<DateOnly, Money> dateAddons)
{
    public Money BaseFee { get; } = baseFee;
    // Snapshot server configuration so a caller cannot mutate the rules during a quote.
    private readonly Dictionary<DateOnly, Money> _addons = new(dateAddons);
    public Money AddonFor(DateOnly date) => _addons.GetValueOrDefault(date);
}

public sealed record PriceQuote
{
    public Money BaseFee { get; }
    public Money Addon { get; }
    public Money Discount { get; }
    public Money Total { get; }
    public Guid? VoucherId { get; }
    public PriceQuote(Money baseFee, Money addon, Money discount, Guid? voucherId = null)
    {
        BaseFee = baseFee; Addon = addon; Discount = discount;
        Total = baseFee + addon - discount;
        VoucherId = voucherId;
    }
}

public sealed record PricingResult(PriceQuote? Quote, string? Reason)
{
    public bool IsSuccess => Quote is not null;
}

public static class PriceCalculator
{
    public static PricingResult Calculate(PricingPolicy policy, DateOnly date, Vouchers.Voucher? voucher,
        Vouchers.VoucherUsage usage, DateTimeOffset now)
    {
        var addon = policy.AddonFor(date);
        var subtotal = policy.BaseFee + addon;
        if (voucher is not null)
        {
            var eligibility = voucher.CheckEligibility(now, usage);
            if (!eligibility.IsSuccess) return new(null, eligibility.Reason);
        }
        var discount = voucher?.DiscountFor(subtotal) ?? new Money(0);
        var quote = new PriceQuote(policy.BaseFee, addon, discount, voucher?.Id);
        return quote.Total.Minor == 0 ? new(null, "Checkout total must be greater than zero.") : new(quote, null);
    }
}
