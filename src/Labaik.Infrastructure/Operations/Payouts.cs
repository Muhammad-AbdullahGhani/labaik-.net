using System.Globalization;
using System.Text;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Operations;

public sealed class Earning
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid PerformerId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Status { get; set; } = "Available";
    public DateTimeOffset CompletedAt { get; set; }
}
public sealed class PayoutBatch
{
    public Guid Id { get; set; }
    public string RequestKey { get; set; } = null!;
    public Guid PerformerId { get; set; }
    public DateOnly WeekStart { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Status { get; set; } = "Pending";
    public long TotalMinor { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }
    public string? BankReference { get; set; }
    public string? DestinationIban { get; set; }
}
public sealed class PayoutItem
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid EarningId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Status { get; set; } = "Pending";
}
public sealed class PayoutPolicy
{
    public long PerformerFeeMinor { get; init; } = 7000;
    public Dictionary<string,string> Accounts { get; init; } = new();
}
public sealed class CompletionService(AppDbContext db, WorkQueue queue, PayoutPolicy policy)
{
    public async Task MoveAsync(Guid bookingId, Guid actorId, BookingState target, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Shared with assignment/cancellation; this also serializes dispute against payout claims.
        await AssignmentLock.TakeAsync(db, ct);
        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={bookingId} FOR UPDATE").SingleAsync(ct);
        var actor = await db.Users.SingleAsync(x => x.Id == actorId && x.Status == UserStatus.Active, ct);
        var offer = await db.PerformerOffers.SingleOrDefaultAsync(x => x.BookingId == bookingId && (x.Status == "Accepted" || x.Status == "Finished"), ct);
        if (actor.Role == ActorRole.Customer && booking.CustomerId != actorId || actor.Role == ActorRole.Performer && (offer?.PerformerId != actorId || actor.SuspendedAt is not null))
            throw new PaymentConflictException("Not authorized for this booking.");
        if (target is not (BookingState.InProgress or BookingState.ProofSubmitted or BookingState.Completed or BookingState.Disputed)) throw new PaymentConflictException("Unsupported operation.");
        if (booking.State == target && target == BookingState.Completed && actor.Role is ActorRole.Supervisor or ActorRole.Admin) { await tx.CommitAsync(ct); return; }
        if (target is BookingState.ProofSubmitted or BookingState.Completed)
        {
            var approved = target == BookingState.Completed;
            if (offer is null || !await db.StoredFiles.AnyAsync(x => x.BookingId == bookingId && x.OwnerId == offer.PerformerId && x.Kind == "Proof" && (x.Status == "Approved" || !approved && x.Status == "Uploaded"), ct))
                throw new PaymentConflictException("Uploaded proof is required; completion requires approved proof.");
        }
        var result = booking.TryTransition(target, actor.Role); if (!result.IsSuccess) throw new PaymentConflictException(result.Reason!);
        if (target == BookingState.Completed)
        {
            if (policy.PerformerFeeMinor <= 0) throw new PaymentConflictException("Performer fee must be positive.");
            if (!await db.Earnings.AnyAsync(x => x.BookingId == bookingId, ct))
                db.Earnings.Add(new Earning { Id = Guid.NewGuid(), BookingId = bookingId, PerformerId = offer!.PerformerId, AmountMinor = Math.Min(policy.PerformerFeeMinor, booking.TotalMinor), CompletedAt = await queue.Now(ct) });
            offer!.Status = "Finished";
            if (!await db.OutboxMessages.AnyAsync(x => x.DedupKey == $"completed:{bookingId:N}",ct))
                Events.Add(db,bookingId,"BookingCompleted",$"completed:{bookingId:N}",new { BookingId=bookingId },await queue.Now(ct));
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
public sealed class PayoutService(AppDbContext db, WorkQueue queue, PayoutPolicy policy)
{
    private async Task Authorize(Guid actorId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actorId && x.Status == UserStatus.Active && (x.Role == ActorRole.Accountant || x.Role == ActorRole.Admin), ct)) throw new PaymentConflictException("Accountant required.");
    }
    public async Task<PayoutBatch?> CreateAsync(Guid performerId, DateOnly weekStart, string key, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || weekStart.DayOfWeek != DayOfWeek.Monday) throw new PaymentConflictException("A request key and Monday week start are required.");
        await Authorize(actorId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await AssignmentLock.TakeAsync(db, ct);
        var existing = await db.PayoutBatches.SingleOrDefaultAsync(x => x.RequestKey == key, ct);
        if (existing is not null)
        {
            if (existing.PerformerId != performerId || existing.WeekStart != weekStart) throw new PaymentConflictException("Request key payload changed.");
            await tx.CommitAsync(ct); return existing;
        }
        var end = new DateTimeOffset(weekStart.AddDays(7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (end > await queue.Now(ct)) throw new PaymentConflictException("Only closed UTC weeks can be paid.");
        // Include older released earnings so a failed historical batch can be retried next week.
        var earnings = await db.Earnings.FromSqlInterpolated($"""
            SELECT e.* FROM app."Earnings" e JOIN app."Bookings" b ON b."Id"=e."BookingId"
            WHERE e."PerformerId"={performerId} AND e."Status"='Available' AND e."CompletedAt"<{end} AND b."State"='Completed'
            ORDER BY e."Id" FOR UPDATE OF e SKIP LOCKED
            """).ToListAsync(ct);
        if (earnings.Count == 0) { await tx.CommitAsync(ct); return null; }
        var batch = new PayoutBatch { Id = Guid.NewGuid(), RequestKey = key, PerformerId = performerId, WeekStart = weekStart };
        foreach (var earning in earnings)
        {
            batch.TotalMinor = checked(batch.TotalMinor + earning.AmountMinor); earning.Status = "Reserved";
            db.PayoutItems.Add(new PayoutItem { Id = Guid.NewGuid(), BatchId = batch.Id, EarningId = earning.Id, AmountMinor = earning.AmountMinor });
        }
        db.PayoutBatches.Add(batch); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return batch;
    }
    public async Task ConfirmAsync(Guid batchId, string status, string? reference, Guid actorId, CancellationToken ct = default)
    {
        await Authorize(actorId, ct);
        if (status is not ("Paid" or "Failed") || string.IsNullOrWhiteSpace(reference) || reference.Length > 128 || reference.Any(char.IsControl)) throw new PaymentConflictException("A confirmed bank result and reference are required; unknown outcomes stay Pending.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await AssignmentLock.TakeAsync(db, ct);
        var batch = await db.PayoutBatches.FromSqlInterpolated($"SELECT * FROM app.\"PayoutBatches\" WHERE \"Id\"={batchId} FOR UPDATE").SingleAsync(ct);
        if (batch.Status != "Pending")
        {
            if (batch.Status != status || batch.BankReference != reference) throw new PaymentConflictException("Batch already has a different final result.");
            await tx.CommitAsync(ct); return;
        }
        if (status == "Paid" && batch.ExportedAt is null) throw new PaymentConflictException("Export the batch before confirming bank payment.");
        if (await db.PayoutBatches.AnyAsync(x => x.BankReference == reference && x.Id != batchId, ct)) throw new PaymentConflictException("Bank reference already used.");
        batch.Status = status; batch.BankReference = reference;
        var items = await db.PayoutItems.Where(x => x.BatchId == batchId).ToListAsync(ct);
        foreach (var item in items)
        {
            var earning = await db.Earnings.SingleAsync(x => x.Id == item.EarningId, ct);
            item.Status = status; earning.Status = status == "Paid" ? "Paid" : "Available";
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<byte[]> ExportAsync(Guid batchId, Guid actorId, CancellationToken ct = default)
    {
        await Authorize(actorId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await AssignmentLock.TakeAsync(db, ct);
        var batch = await db.PayoutBatches.FromSqlInterpolated($"SELECT * FROM app.\"PayoutBatches\" WHERE \"Id\"={batchId} FOR UPDATE").SingleAsync(ct);
        if (batch.Status == "Failed") throw new PaymentConflictException("Failed batches cannot be exported.");
        var account = batch.DestinationIban;
        if (account is null && (!policy.Accounts.TryGetValue(batch.PerformerId.ToString(), out account) || !ValidIban(account))) throw new PaymentConflictException("Configure a valid Saudi IBAN for this performer before export.");
        var rows = await (from item in db.PayoutItems join earning in db.Earnings on item.EarningId equals earning.Id join booking in db.Bookings on earning.BookingId equals booking.Id where item.BatchId == batchId select new { item, booking.State }).OrderBy(x => x.item.EarningId).ToListAsync(ct);
        if (batch.ExportedAt is null && rows.Any(x => x.State != BookingState.Completed)) throw new PaymentConflictException("A booking is disputed; resolve it before export.");
        batch.ExportedAt ??= await queue.Now(ct);
        batch.DestinationIban ??= account;
        // Export is a deterministic instruction, never proof that the bank transferred money.
        var csv = new StringBuilder("batch_id,earning_id,iban,currency,amount\r\n");
        foreach (var row in rows) csv.Append(CultureInfo.InvariantCulture, $"{batch.Id:D},{row.item.EarningId:D},{account},SAR,{row.item.AmountMinor / 100}.{row.item.AmountMinor % 100:D2}\r\n");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Encoding.UTF8.GetBytes(csv.ToString());
    }
    private static bool ValidIban(string value)
    {
        if (value.Length != 24 || !value.StartsWith("SA", StringComparison.Ordinal) || value.Skip(2).Any(c => c is < '0' or > '9')) return false;
        var moved = value[4..] + "2810" + value.Substring(2,2); var remainder = 0;
        foreach (var digit in moved) remainder = (remainder * 10 + digit - '0') % 97;
        return remainder == 1;
    }
}
