namespace Labaik.Infrastructure.Payments;

public enum AttemptStatus { Creating, Pending, Unknown, Succeeded, Failed }
public enum InboxStatus { Pending, Processed, Rejected }
public enum WorkStatus { Pending, Processing, Completed, DeadLetter }
public enum WorkKind { RecoverCheckout, Inbox, Outbox, Refund }
public sealed class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public int Sequence { get; set; }
    public string RequestKey { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string GatewayKey { get; set; } = null!;
    public Guid? CheckoutId { get; set; }
    public AttemptStatus Status { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
}
public sealed class CapturedPayment
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid GatewayTransactionId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
    public DateTimeOffset CapturedAt { get; set; }
}
public sealed class WebhookInbox
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "fake-hyperpay";
    public Guid EventId { get; set; }
    public string PayloadHash { get; set; } = null!;
    public string Signature { get; set; } = null!;
    public byte[] EncryptedBody { get; set; } = null!;
    public InboxStatus Status { get; set; }
    public bool HasConflict { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string DedupKey { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset? PublishedAt { get; set; }
}
public sealed class JobRun
{
    public Guid Id { get; set; }
    public string JobName { get; set; } = null!;
    public DateTimeOffset ScheduledFor { get; set; }
    public WorkKind Kind { get; set; }
    public Guid EntityId { get; set; }
    public WorkStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long ClaimVersion { get; set; }
    public string? LastError { get; set; }
}

public sealed class RefundRequest
{
    public Guid Id { get; set; }
    public Guid CapturedPaymentId { get; set; }
    public string GatewayKey { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public Guid? GatewayRefundId { get; set; }
}
