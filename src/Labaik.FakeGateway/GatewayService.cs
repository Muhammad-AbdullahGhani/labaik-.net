using System.Security.Cryptography;
using System.Text.Json;
using Labaik.Application.Payments;
using Labaik.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace Labaik.FakeGateway;

public sealed class GatewayService(GatewayDbContext db, WebhookCodec codec)
{
    public static GatewaySnapshot Snapshot(GatewayCheckout checkout) => new(checkout.Id, checkout.BookingId,
        checkout.IdempotencyKey, checkout.AmountMinor, checkout.Currency, checkout.Status, checkout.TransactionId);
    public async Task<GatewaySnapshot> CreateAsync(CheckoutCommand command, CancellationToken ct)
    {
        if (command.BookingId == Guid.Empty || string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128 || command.AmountMinor <= 0 || command.Currency != "SAR")
            throw new ArgumentException("Invalid checkout.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));
        var id = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO fake_gateway."Checkouts" ("Id","BookingId","IdempotencyKey","RequestHash","AmountMinor","Currency","Status")
            VALUES ({id},{command.BookingId},{command.IdempotencyKey},{hash},{command.AmountMinor},{command.Currency},'Pending')
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """, ct);
        var checkout = await db.Checkouts.SingleAsync(x => x.IdempotencyKey == command.IdempotencyKey, ct);
        if (checkout.RequestHash != hash) throw new PaymentConflictException("Gateway key reused with changed payload.");
        if (inserted == 1) await AddEvent(checkout, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Snapshot(checkout);
    }
    public async Task<GatewaySnapshot> PayAsync(Guid id, string outcome, CancellationToken ct)
    {
        if (outcome is not ("Succeeded" or "Failed" or "Pending")) throw new ArgumentException("Invalid outcome.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var checkout = await db.Checkouts.FromSqlInterpolated($"SELECT * FROM fake_gateway.\"Checkouts\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        if (checkout.Status != "Pending")
        {
            // A final no-charge failure can never capture later; unknown timeouts are not final failures.
            if (checkout.Status != outcome) throw new PaymentConflictException("Checkout outcome is final.");
            return Snapshot(checkout);
        }
        if (outcome == "Succeeded")
        {
            var transactionId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO fake_gateway."Charges" ("Id","BookingId","CheckoutId","AmountMinor","Currency")
                VALUES ({transactionId},{checkout.BookingId},{id},{checkout.AmountMinor},{checkout.Currency})
                ON CONFLICT ("BookingId") DO NOTHING
                """, ct);
            var charge = await db.Charges.SingleAsync(x => x.BookingId == checkout.BookingId, ct);
            if (charge.CheckoutId != id) throw new PaymentConflictException("Booking was already charged.");
            checkout.TransactionId = charge.Id;
        }
        checkout.Status = outcome;
        await AddEvent(checkout, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Snapshot(checkout);
    }
    public async Task<RefundSnapshot> RefundAsync(RefundCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var charge = await db.Charges.FromSqlInterpolated($"SELECT * FROM fake_gateway.\"Charges\" WHERE \"Id\"={command.TransactionId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        if (command.AmountMinor != charge.AmountMinor || command.Currency != charge.Currency || string.IsNullOrWhiteSpace(command.Key))
            throw new PaymentConflictException("Refund must match the complete original charge.");
        var refund = await db.Refunds.SingleOrDefaultAsync(x => x.TransactionId == charge.Id, ct);
        if (refund is not null && refund.Key != command.Key) throw new PaymentConflictException("Charge already refunded under another key.");
        if (refund is null) { refund = new GatewayRefund { Id = Guid.NewGuid(), TransactionId = charge.Id, Key = command.Key, AmountMinor = charge.AmountMinor, Currency = charge.Currency }; db.Refunds.Add(refund); }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(refund.Id, refund.TransactionId, refund.Key, refund.AmountMinor, refund.Currency, "Succeeded");
    }
    private async Task AddEvent(GatewayCheckout checkout, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        // Persist wire bytes once. Duplicate/retried delivery reuses the same event ID, nonce and ciphertext.
        var notice = new PaymentNotice(id, checkout.Id, checkout.BookingId, checkout.IdempotencyKey, checkout.AmountMinor, checkout.Currency, checkout.Status);
        db.Events.Add(new GatewayEvent { Id = id, CheckoutId = checkout.Id, Body = codec.Encrypt(notice), AvailableAt = now });
    }
}
