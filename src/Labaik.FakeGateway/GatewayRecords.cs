namespace Labaik.FakeGateway;

public sealed class GatewayCheckout
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Status { get; set; } = "Pending";
    public Guid? TransactionId { get; set; }
}
public sealed class GatewayCharge
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid CheckoutId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
}
public sealed class GatewayEvent
{
    public Guid Id { get; set; }
    public Guid CheckoutId { get; set; }
    public byte[] Body { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long ClaimVersion { get; set; }
}
public sealed class GatewaySimulation
{
    public int CreateDelayMs { get; set; }
    public int QueryDelayMs { get; set; }
    public int PayResponseDelayMs { get; set; }
    public bool FailCreateResponse { get; set; }
    public bool FailRefundResponse { get; set; }
    public bool FailPayResponse { get; set; }
    public bool QueryUnavailable { get; set; }
    public int DeliveryCopies { get; set; } = 1;
}

public sealed class GatewayRefund
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string Key { get; set; } = null!;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
}
