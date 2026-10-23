namespace Labaik.Domain.Pricing;

public readonly record struct Money
{
    public long Minor { get; }
    public string Currency => "SAR";
    public Money(long minor, string currency = "SAR")
    {
        if (currency != "SAR") throw new ArgumentException("Only SAR is supported.", nameof(currency));
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
        Minor = minor;
    }
    public static Money operator +(Money left, Money right) => new(checked(left.Minor + right.Minor));
    public static Money operator -(Money left, Money right) => new(checked(left.Minor - right.Minor));
}
