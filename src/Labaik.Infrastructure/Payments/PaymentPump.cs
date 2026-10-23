using System.Text.Json;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Payments;

public sealed class PaymentPump(AppDbContext db, WorkQueue queue, IPaymentGateway gateway, WebhookCodec codec, IPaymentEventPublisher publisher, BookingMoney money)
{
    public async Task<bool> RunOnceAsync(WorkKind? kind = null, CancellationToken ct = default)
    {
        var claim = await queue.ClaimAsync(kind, ct);
        if (claim is null) return false;
        try
        {
            if (claim.Kind == WorkKind.Refund) await RefundAsync(claim, ct);
            else if (claim.Kind == WorkKind.Outbox) await PublishAsync(claim, ct);
            else await VerifyAsync(claim, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) { await queue.FailAsync(claim, ct, error.GetType().Name); }
        return true;
    }
    private async Task PublishAsync(WorkClaim claim, CancellationToken ct)
    {
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == claim.EntityId, ct);
        await publisher.PublishAsync(new(message.Id, message.BookingId, message.Type, message.Payload), ct);
        // External send happens outside the transaction. After a crash it can repeat with the same message ID.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await queue.LockAsync(claim, ct);
        if (job is null) return;
        var row = await db.OutboxMessages.SingleAsync(x => x.Id == message.Id, ct);
        row.PublishedAt = await queue.Now(ct); WorkQueue.Complete(job);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task VerifyAsync(WorkClaim claim, CancellationToken ct)
    {
        PaymentNotice? notice = null;
        PaymentAttempt? original;
        if (claim.Kind == WorkKind.Inbox)
        {
            var inbox = await db.WebhookInbox.AsNoTracking().SingleAsync(x => x.Id == claim.EntityId, ct);
            notice = codec.VerifyAndDecrypt(inbox.EncryptedBody, inbox.Signature);
            original = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.GatewayKey == notice.IdempotencyKey, ct);
        }
        else original = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claim.EntityId, ct);
        if (original is null) throw new PaymentDependencyException();
        // Re-query even if the webhook says success. The callback/redirect never proves a capture.
        var checkoutId = notice?.CheckoutId ?? original.CheckoutId;
        if (checkoutId is null)
        {
            var created = await gateway.CreateAsync(new(original.BookingId, original.GatewayKey, original.AmountMinor, original.Currency), ct);
            checkoutId = created.CheckoutId;
        }
        var snapshot = await gateway.QueryAsync(checkoutId.Value, ct);
        if (snapshot.Status is not ("Pending" or "Succeeded" or "Failed")) throw new PaymentDependencyException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await queue.LockAsync(claim, ct);
        if (job is null) return; // Lease expired: a newer owner alone can commit application effects.
        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={original.BookingId} FOR UPDATE").SingleAsync(ct);
        var attempt = await db.PaymentAttempts.SingleAsync(x => x.Id == original.Id, ct);
        WebhookInbox? received = null;
        if (notice is not null)
            received = await db.WebhookInbox.FromSqlInterpolated($"SELECT * FROM app.\"WebhookInbox\" WHERE \"Id\"={claim.EntityId} FOR UPDATE").SingleAsync(ct);
        var now = await queue.Now(ct);
        var matches = snapshot.CheckoutId != Guid.Empty && snapshot.CheckoutId == checkoutId && snapshot.BookingId == attempt.BookingId &&
            snapshot.IdempotencyKey == attempt.GatewayKey && snapshot.AmountMinor == attempt.AmountMinor && snapshot.Currency == attempt.Currency &&
            (attempt.CheckoutId is null || attempt.CheckoutId == snapshot.CheckoutId) && booking.TotalMinor == attempt.AmountMinor && booking.Currency == attempt.Currency &&
            (notice is null || notice.EventId == received!.EventId && notice.BookingId == attempt.BookingId &&
                notice.AmountMinor == attempt.AmountMinor && notice.Currency == attempt.Currency && notice.CheckoutId == snapshot.CheckoutId);
        if (!matches || received?.HasConflict == true || snapshot.Status == "Succeeded" && (snapshot.TransactionId is null || snapshot.TransactionId == Guid.Empty))
        {
            if (received is not null) received.Status = InboxStatus.Rejected;
            await AddOutbox(booking.Id, "PaymentMismatch", $"mismatch:{claim.EntityId:N}", new { AttemptId = attempt.Id }, now, ct);
            WorkQueue.Complete(job);
            if (claim.Kind == WorkKind.RecoverCheckout) { job.Status = WorkStatus.DeadLetter; job.LastError = "PaymentMismatch"; }
        }
        else
        {
            attempt.CheckoutId ??= snapshot.CheckoutId;
            if (snapshot.Status == "Succeeded")
            {
                var capture = await db.CapturedPayments.SingleOrDefaultAsync(x => x.BookingId == booking.Id, ct);
                if (capture is not null && capture.GatewayTransactionId != snapshot.TransactionId)
                    throw new PaymentDependencyException(); // Never credit a second provider transaction as the same payment.
                if (capture is null)
                {
                    capture = new CapturedPayment { Id = Guid.NewGuid(), BookingId = booking.Id, AttemptId = attempt.Id,
                        GatewayTransactionId = snapshot.TransactionId!.Value, AmountMinor = snapshot.AmountMinor, Currency = snapshot.Currency, CapturedAt = now };
                    db.CapturedPayments.Add(capture);
                }
                attempt.Status = AttemptStatus.Succeeded;
                if (booking.State == BookingState.PendingPayment)
                {
                    if (await money.AllocateAsync(booking, ct))
                    {
                        var transition = booking.TryTransition(BookingState.Paid, ActorRole.System);
                        if (!transition.IsSuccess) throw new PaymentConflictException(transition.Reason!);
                        await money.ConsumeVoucherAsync(booking, ct);
                        await AddOutbox(booking.Id, "BookingPaid", $"paid:{booking.Id:N}", new { PaymentId = capture.Id }, now, ct);
                    }
                    else
                    {
                        _ = booking.Cancel("DateFull", ActorRole.System);
                        await money.ReleaseVoucherAsync(booking, now, ct);
                    }
                }
                if (booking.State == BookingState.Cancelled)
                {
                    await money.RequestRefundAsync(capture, booking.CancellationReason ?? "LatePayment", now, ct);
                    await AddOutbox(booking.Id, "RefundRequired", $"refund-required:{capture.Id:N}", new { PaymentId = capture.Id }, now, ct);
                    await AddOutbox(booking.Id, "AccountantAlert", $"late-payment:{capture.Id:N}", new { PaymentId = capture.Id }, now, ct);
                }
            }
            else if (attempt.Status is not (AttemptStatus.Succeeded or AttemptStatus.Failed))
            {
                attempt.Status = snapshot.Status == "Failed" ? AttemptStatus.Failed : AttemptStatus.Pending;
                if (attempt.Status == AttemptStatus.Failed) await money.ReleaseVoucherAsync(booking, now, ct);
            }
            if (received is not null) received.Status = InboxStatus.Processed;
            if (claim.Kind == WorkKind.RecoverCheckout && attempt.Status is not (AttemptStatus.Succeeded or AttemptStatus.Failed))
                queue.Retry(job, now, "AwaitingPayment", false);
            else WorkQueue.Complete(job);
        }
        // Capture, booking version/state, outbox intents and inbox acknowledgement are all-or-nothing.
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task RefundAsync(WorkClaim claim, CancellationToken ct)
    {
        var request = await db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == claim.EntityId, ct);
        var capture = await db.CapturedPayments.AsNoTracking().SingleAsync(x => x.Id == request.CapturedPaymentId, ct);
        var result = await gateway.RefundAsync(capture.GatewayTransactionId, request.GatewayKey, capture.AmountMinor, capture.Currency, ct);
        if (result.Status != "Succeeded" || result.RefundId == Guid.Empty || result.TransactionId != capture.GatewayTransactionId || result.Key != request.GatewayKey || result.AmountMinor != capture.AmountMinor || result.Currency != capture.Currency)
            throw new PaymentDependencyException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await queue.LockAsync(claim, ct); if (job is null) return;
        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={capture.BookingId} FOR UPDATE").SingleAsync(ct);
        var refund = await db.RefundRequests.SingleAsync(x => x.Id == request.Id, ct);
        refund.Status = "Succeeded"; refund.GatewayRefundId = result.RefundId;
        if (booking.State == BookingState.Cancelled) _ = booking.TryTransition(BookingState.Refunded, ActorRole.System);
        await money.ReleaseVoucherAsync(booking, await queue.Now(ct), ct); WorkQueue.Complete(job);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task AddOutbox(Guid bookingId, string type, string key, object payload, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.OutboxMessages.AnyAsync(x => x.DedupKey == key, ct)) return;
        var id = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage { Id = id, BookingId = bookingId, Type = type, DedupKey = key, Payload = JsonSerializer.Serialize(payload) });
        WorkQueue.Enqueue(db, WorkKind.Outbox, id, now);
    }
}
