using System.Security.Cryptography;
using System.Text;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Payments;

public sealed record CheckoutAttemptView(Guid Id, Guid? CheckoutId, AttemptStatus Status);
public sealed class PaymentCheckout(AppDbContext db, IPaymentGateway gateway)
{
    public async Task<CheckoutAttemptView> CheckoutAsync(Guid bookingId, Guid actorId, string requestKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 128) throw new PaymentConflictException("Invalid request key.");
        Guid attemptId;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={bookingId} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new PaymentConflictException("Booking not found.");
            var actor = await db.Users.SingleOrDefaultAsync(x => x.Id == actorId, ct);
            if (booking.CustomerId != actorId || actor is null || actor.Status != UserStatus.Active || actor.Role is not (ActorRole.Customer or ActorRole.Admin))
                throw new PaymentConflictException("Booking is not owned by this customer.");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{booking.Id:N}|{booking.ServiceDate:yyyy-MM-dd}|{booking.TotalMinor}|{booking.Currency}"))));
            var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(x => x.BookingId == bookingId && x.RequestKey == requestKey, ct)
                ?? await db.PaymentAttempts.SingleOrDefaultAsync(x => x.BookingId == bookingId && x.Status != AttemptStatus.Failed, ct);
            if (attempt is not null && attempt.RequestHash != hash) throw new PaymentConflictException("Checkout payload changed.");
            if (attempt is null)
            {
                if (booking.State == BookingState.Draft)
                {
                    var transition = booking.TryTransition(BookingState.PendingPayment, actor.Role);
                    if (!transition.IsSuccess) throw new PaymentConflictException(transition.Reason!);
                }
                if (booking.State != BookingState.PendingPayment) throw new PaymentConflictException("Booking cannot be checked out.");
                var id = Guid.NewGuid();
                attempt = new PaymentAttempt { Id = id, BookingId = bookingId, RequestKey = requestKey, RequestHash = hash,
                    GatewayKey = $"checkout:{id:N}", AmountMinor = booking.TotalMinor, Currency = booking.Currency,
                    Sequence = (await db.PaymentAttempts.Where(x => x.BookingId == bookingId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1 };
                db.PaymentAttempts.Add(attempt);
                WorkQueue.Enqueue(db, WorkKind.RecoverCheckout, id, await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct));
            }
            attemptId = attempt.Id;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        var intent = await db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == attemptId, ct);
        if (intent.CheckoutId is not null || intent.Status is AttemptStatus.Succeeded or AttemptStatus.Failed)
            return new(intent.Id, intent.CheckoutId, intent.Status);
        GatewaySnapshot? snapshot = null;
        try { snapshot = await gateway.CreateAsync(new(intent.BookingId, intent.GatewayKey, intent.AmountMinor, intent.Currency), ct); }
        catch (Exception error) when (error is PaymentDependencyException or HttpRequestException or TaskCanceledException)
        { /* Unknown is blocking, including a lost response after the provider already committed. */ }
        await using (var tx = await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            _ = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={bookingId} FOR UPDATE").SingleAsync();
            var attempt = await db.PaymentAttempts.SingleAsync(x => x.Id == attemptId);
            if (attempt.Status is AttemptStatus.Creating or AttemptStatus.Unknown)
            {
                if (snapshot is not null && snapshot.CheckoutId != Guid.Empty && snapshot.BookingId == attempt.BookingId &&
                    snapshot.IdempotencyKey == attempt.GatewayKey && snapshot.AmountMinor == attempt.AmountMinor && snapshot.Currency == attempt.Currency)
                { attempt.CheckoutId = snapshot.CheckoutId; attempt.Status = AttemptStatus.Pending; }
                else attempt.Status = AttemptStatus.Unknown;
            }
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return new(attempt.Id, attempt.CheckoutId, attempt.Status);
        }
    }
}
