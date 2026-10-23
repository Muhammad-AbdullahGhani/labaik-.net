namespace Labaik.Application.Payments;

public sealed record CheckoutCommand(Guid BookingId, string IdempotencyKey, long AmountMinor, string Currency);
public sealed record GatewaySnapshot(Guid CheckoutId, Guid BookingId, string IdempotencyKey, long AmountMinor,
    string Currency, string Status, Guid? TransactionId);
public sealed record PaymentNotice(Guid EventId, Guid CheckoutId, Guid BookingId, string IdempotencyKey,
    long AmountMinor, string Currency, string Status);
public interface IPaymentGateway
{
    Task<GatewaySnapshot> CreateAsync(CheckoutCommand command, CancellationToken ct);
    Task<RefundSnapshot> RefundAsync(Guid transactionId, string key, long amountMinor, string currency, CancellationToken ct)
        => throw new PaymentDependencyException();
    Task<GatewaySnapshot> QueryAsync(Guid checkoutId, CancellationToken ct);
    Task<GatewayLedgerPage> ListAsync(Guid? after, CancellationToken ct) => throw new PaymentDependencyException();
}
public sealed record PaymentEvent(Guid Id, Guid BookingId, string Type, string Payload);
public interface IPaymentEventPublisher
{
    Task PublishAsync(PaymentEvent message, CancellationToken ct);
}
public sealed class PaymentConflictException(string reason) : Exception(reason);
public sealed class PaymentDependencyException : Exception;

public sealed record RefundCommand(Guid TransactionId, string Key, long AmountMinor, string Currency);
public sealed record RefundSnapshot(Guid RefundId, Guid TransactionId, string Key, long AmountMinor, string Currency, string Status);
public sealed record GatewayLedgerRow(Guid CheckoutId, Guid BookingId, Guid TransactionId, long AmountMinor, string Currency, Guid? RefundId);
public sealed record GatewayLedgerPage(GatewayLedgerRow[] Rows, Guid? Next);
