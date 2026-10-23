using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Operations;

public sealed class PerformerOffer
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid PerformerId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
public static class AssignmentLock
{
    public static async Task TakeAsync(AppDbContext db, CancellationToken ct)
    {
        // A short ordinary row lock serializes the small global round-robin, acceptance and expiry decisions.
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001"); var epoch = DateTimeOffset.UnixEpoch;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."JobRuns" ("Id","JobName","ScheduledFor","Kind","EntityId","Status","Attempts","AvailableAt","ClaimVersion")
            VALUES ({id},'assignment-mutex',{epoch},'Assign',{Guid.Empty},'Completed',0,{epoch},0)
            ON CONFLICT ("JobName","ScheduledFor") DO NOTHING
            """, ct);
        _ = await db.JobRuns.FromSqlRaw("SELECT * FROM app.\"JobRuns\" WHERE \"JobName\"='assignment-mutex' FOR UPDATE").SingleAsync(ct);
    }
}
public sealed class AssignmentService(AppDbContext db, WorkQueue queue)
{
    public async Task HandleAsync(WorkClaim claim, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await queue.LockAsync(claim, ct); if (job is null) return;
        await AssignmentLock.TakeAsync(db, ct);
        if (claim.Kind == WorkKind.Assign)
        {
            var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={claim.EntityId} FOR UPDATE").SingleAsync(ct);
            if (booking.State != BookingState.Paid) WorkQueue.Complete(job);
            else
            {
                var performer = await db.Users.FromSqlRaw("""
                    SELECT * FROM app."Users" u WHERE u."Role"='Performer' AND u."Status"='Active' AND u."SuspendedAt" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM app."PerformerOffers" o WHERE o."PerformerId"=u."Id" AND o."Status" IN ('Pending','Accepted'))
                    ORDER BY u."LastOfferedSequence",u."Id" LIMIT 1 FOR UPDATE
                    """).SingleOrDefaultAsync(ct);
                var now = await queue.Now(ct);
                if (performer is null) queue.Retry(job, now, "AwaitingPerformer", false);
                else
                {
                    var sequence = (await db.Users.MaxAsync(x => (long?)x.LastOfferedSequence, ct) ?? 0) + 1;
                    db.Entry(performer).Property(x => x.LastOfferedSequence).CurrentValue = sequence;
                    var offer = new PerformerOffer { Id = Guid.NewGuid(), BookingId = booking.Id, PerformerId = performer.Id, ExpiresAt = now.AddMinutes(2) };
                    db.PerformerOffers.Add(offer);
                    var result = booking.TryTransition(BookingState.Offered, ActorRole.System);
                    if (!result.IsSuccess) throw new PaymentConflictException(result.Reason!);
                    WorkQueue.Enqueue(db, WorkKind.ExpireOffer, offer.Id, offer.ExpiresAt); WorkQueue.Complete(job);
                }
            }
        }
        else
        {
            var info = await db.PerformerOffers.AsNoTracking().SingleAsync(x => x.Id == claim.EntityId, ct);
            var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={info.BookingId} FOR UPDATE").SingleAsync(ct);
            var offer = await db.PerformerOffers.SingleAsync(x => x.Id == info.Id, ct); var now = await queue.Now(ct);
            if (offer.Status == "Pending" && offer.ExpiresAt <= now)
            {
                offer.Status = "Expired";
                if (booking.State == BookingState.Offered)
                {
                    _ = booking.TryTransition(BookingState.Paid, ActorRole.System);
                    WorkQueue.Enqueue(db, WorkKind.Assign, booking.Id, now, offer.Id.ToString("N"));
                }
            }
            if (offer.Status == "Pending") queue.Retry(job, now, "AwaitingDeadline", false); else WorkQueue.Complete(job);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<PerformerOffer> AcceptAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await AssignmentLock.TakeAsync(db, ct);
        var info = await db.PerformerOffers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={info.BookingId} FOR UPDATE").SingleAsync(ct);
        var offer = await db.PerformerOffers.SingleAsync(x => x.Id == id, ct);
        var actor = await db.Users.SingleAsync(x => x.Id == actorId, ct);
        if (offer.PerformerId != actorId || actor.Role != ActorRole.Performer || actor.Status != UserStatus.Active || actor.SuspendedAt is not null) throw new PaymentConflictException("Offer is not available to this performer.");
        if (offer.Status == "Accepted") return offer;
        var now = await queue.Now(ct);
        if (offer.Status != "Pending" || offer.ExpiresAt <= now) throw new PaymentConflictException("Offer expired or already resolved.");
        var result = booking.TryTransition(BookingState.Accepted, ActorRole.Performer); if (!result.IsSuccess) throw new PaymentConflictException(result.Reason!);
        offer.Status = "Accepted"; offer.AcceptedAt = now; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return offer;
    }
    public async Task SuspendAsync(Guid performerId, Guid actorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await AssignmentLock.TakeAsync(db, ct);
        var actor = await db.Users.SingleAsync(x => x.Id == actorId, ct);
        if (actor.Status != UserStatus.Active || actor.Role is not (ActorRole.Admin or ActorRole.Supervisor)) throw new PaymentConflictException("Supervisor required.");
        var performer = await db.Users.SingleAsync(x => x.Id == performerId && x.Role == ActorRole.Performer, ct);
        db.Entry(performer).Property(x => x.SuspendedAt).CurrentValue = await queue.Now(ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
