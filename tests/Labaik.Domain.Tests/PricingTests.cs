using Labaik.Domain.Pricing;
using Labaik.Domain.Vouchers;
using Xunit;

namespace Labaik.Domain.Tests;

public sealed class PricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
    private static Voucher Percent(int bp) => new(Guid.NewGuid(), "test", VoucherKind.Percentage, null, bp, 5, 2, Now.AddDays(1));
    [Fact]
    public void Money_is_SAR_nonnegative_and_checked()
    {
        Assert.Equal("SAR", default(Money).Currency);
        Assert.Throws<ArgumentException>(() => new Money(1, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1));
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(1) - new Money(2));
        Assert.Equal(new Money(7), new Money(10) - new Money(3));
    }
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(101, 1000, 10)]
    [InlineData(9999, 1, 0)]
    [InlineData(10001, 9999, 9999)]
    [InlineData(long.MaxValue, 10000, long.MaxValue)]
    [InlineData(long.MaxValue, 5000, 4611686018427387903L)]
    public void Percentage_is_exact_floor_without_intermediate_overflow(long subtotal, int bp, long expected) =>
        Assert.Equal(expected, Percent(bp).DiscountFor(new Money(subtotal)).Minor);

    [Fact]
    public void Configuration_addons_and_voucher_produce_a_stable_quote()
    {
        var date = new DateOnly(2026, 12, 1);
        var addons = new Dictionary<DateOnly, Money> { [date] = new Money(101) };
        var policy = new PricingPolicy(new Money(1000), addons);
        addons[date] = new Money(999);
        var result = PriceCalculator.Calculate(policy, date, Percent(1000), default, Now);
        Assert.True(result.IsSuccess);
        Assert.Equal(101, result.Quote!.Addon.Minor);
        Assert.Equal(110, result.Quote.Discount.Minor);
        Assert.Equal(991, result.Quote.Total.Minor);
        Assert.Equal(1000, PriceCalculator.Calculate(policy, date.AddDays(1), null, default, Now).Quote!.Total.Minor);
    }
    [Fact]
    public void Fixed_discount_clamps_but_zero_checkout_is_rejected()
    {
        var voucher = new Voucher(Guid.NewGuid(), "fixed", VoucherKind.Fixed, 500, null, 5, 2, Now.AddDays(1));
        Assert.Equal(100, voucher.DiscountFor(new Money(100)).Minor);
        var zero = PriceCalculator.Calculate(new PricingPolicy(new Money(100), new Dictionary<DateOnly, Money>()), default, voucher, default, Now);
        Assert.False(zero.IsSuccess);
        Assert.NotNull(zero.Reason);
        Assert.False(PriceCalculator.Calculate(new PricingPolicy(default, new Dictionary<DateOnly, Money>()), default, null, default, Now).IsSuccess);
        Assert.True(PriceCalculator.Calculate(new PricingPolicy(new Money(1), new Dictionary<DateOnly, Money>()), default, null, default, Now).IsSuccess);
    }
    [Fact]
    public void Subtotal_overflow_and_invalid_discount_configuration_are_rejected()
    {
        var date = new DateOnly(2026, 12, 1);
        var policy = new PricingPolicy(new Money(long.MaxValue), new Dictionary<DateOnly, Money> { [date] = new Money(1) });
        Assert.Throws<OverflowException>(() => PriceCalculator.Calculate(policy, date, null, default, Now));
        Assert.Throws<ArgumentException>(() => Percent(-1));
        Assert.Throws<ArgumentException>(() => Percent(10001));
    }
    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(4, 1, 0, true)]
    [InlineData(5, 0, 0, false)]
    [InlineData(2, 2, 0, false)]
    [InlineData(0, 0, 86400, false)]
    [InlineData(0, 0, 86401, false)]
    public void Voucher_expiry_and_global_and_user_limits(int used, int userUsed, int seconds, bool expected)
    {
        var voucher = Percent(1000);
        Assert.Equal(expected, voucher.CheckEligibility(Now.AddSeconds(seconds), new VoucherUsage(used, userUsed)).IsSuccess);
        Assert.Equal(expected, PriceCalculator.Calculate(new PricingPolicy(new Money(100), new Dictionary<DateOnly, Money>()), default,
            voucher, new VoucherUsage(used, userUsed), Now.AddSeconds(seconds)).IsSuccess);
    }
    [Fact]
    public void Disabled_voucher_and_invalid_snapshots_are_rejected()
    {
        var voucher = new Voucher(Guid.NewGuid(), " test ", VoucherKind.Fixed, 10, null, 5, 2, Now.AddDays(1), false);
        Assert.Equal("TEST", voucher.CodeNormalized);
        Assert.False(voucher.CheckEligibility(Now, default).IsSuccess);
        Assert.Throws<ArgumentException>(() => voucher.CheckEligibility(Now, new VoucherUsage(-1, 0)));
        Assert.Throws<ArgumentException>(() => voucher.CheckEligibility(Now, new VoucherUsage(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Voucher(Guid.NewGuid(), "test", VoucherKind.Fixed, 10, null, 0, 2, Now));
        Assert.Throws<ArgumentException>(() => new Voucher(Guid.NewGuid(), "test", VoucherKind.Fixed, null, null, 5, 2, Now));
    }
}
