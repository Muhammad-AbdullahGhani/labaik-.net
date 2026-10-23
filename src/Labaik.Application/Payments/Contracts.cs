namespace Labaik.Application.Payments;

public sealed record CheckoutCommand(Guid BookingId, string IdempotencyKey, long AmountMinor, string Currency);
public sealed record GatewaySnapshot(Guid CheckoutId, Guid BookingId, string IdempotencyKey, long AmountMinor,
    string Currency, string Status, Guid? TransactionId);
public sealed record PaymentNotice(Guid EventId, Guid CheckoutId, Guid BookingId, string IdempotencyKey,
    long AmountMinor, string Currency, string Status);
public interface IPaymentGateway
{
    Task<GatewaySnapshot> CreateAsync(CheckoutCommand command, CancellationToken ct);
    Task<GatewaySnapshot> QueryAsync(Guid checkoutId, CancellationToken ct);
}
public sealed record PaymentEvent(Guid Id, Guid BookingId, string Type, string Payload);
public interface IPaymentEventPublisher
{
    Task PublishAsync(PaymentEvent message, CancellationToken ct);
}
public sealed class PaymentConflictException(string reason) : Exception(reason);
public sealed class PaymentDependencyException : Exception;
