using Labaik.Application.Files;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Operations;

public sealed class StoredFile
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = null!;
    public string ObjectKey { get; set; } = null!;
    public string Status { get; set; } = "PendingUpload";
    public long Size { get; set; }
    public string? ETag { get; set; }
    public string? VersionId { get; set; }
}
public sealed record UploadResult(Guid FileId, Uri Url, DateTimeOffset ExpiresAt);
public sealed class PrivateFileService(AppDbContext db, IPrivateBlobStore storage)
{
    private async Task<User> Actor(Guid id, CancellationToken ct) => await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Status == UserStatus.Active, ct) ?? throw new PaymentConflictException("Not authorized.");
    private async Task<bool> CanAccess(Guid bookingId, string kind, User actor, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bookingId, ct);
        if (booking is null) return false;
        if (actor.Role is ActorRole.Supervisor or ActorRole.Admin || booking.CustomerId == actor.Id) return true;
        return kind == "Proof" && actor.Role == ActorRole.Performer && await db.PerformerOffers.AnyAsync(x => x.BookingId == bookingId && x.PerformerId == actor.Id && (x.Status == "Accepted" || x.Status == "Finished"), ct);
    }
    public async Task<UploadResult> UploadAsync(Guid bookingId, Guid actorId, string kind, CancellationToken ct = default)
    {
        var actor = await Actor(actorId, ct);
        if (kind is not ("Proof" or "Passport") || !await CanAccess(bookingId, kind, actor, ct)) throw new PaymentConflictException("Not authorized.");
        var booking = await db.Bookings.SingleAsync(x => x.Id == bookingId, ct);
        if (kind == "Passport" && booking.CustomerId != actorId || kind == "Proof" && (actor.Role != ActorRole.Performer || actor.SuspendedAt is not null || !await db.PerformerOffers.AnyAsync(x => x.BookingId == bookingId && x.PerformerId == actorId && x.Status == "Accepted", ct)))
            throw new PaymentConflictException("Only the customer uploads passports; assigned performers upload proof.");
        var file = new StoredFile { Id = Guid.NewGuid(), BookingId = bookingId, OwnerId = actorId, Kind = kind };
        file.ObjectKey = $"{kind.ToLowerInvariant()}/{bookingId:N}/{file.Id:N}";
        db.StoredFiles.Add(file); await db.SaveChangesAsync(ct);
        // No storage network call is made while a DB transaction is held.
        var grant = await storage.UploadAsync(file.ObjectKey, ct); return new(file.Id, grant.Url, grant.ExpiresAt);
    }
    public async Task CompleteAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var info = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PaymentConflictException("File not found.");
        var actor = await Actor(actorId, ct);
        if (info.OwnerId != actorId || !await CanAccess(info.BookingId, info.Kind, actor, ct)) throw new PaymentConflictException("Not authorized.");
        var blob = await storage.InspectAsync(info.ObjectKey, ct);
        if (blob.Size is <= 0 or > 52428800 || (info.Kind == "Proof" ? blob.ContentType != "video/mp4" : blob.ContentType is not ("image/jpeg" or "application/pdf"))) throw new PaymentConflictException("Invalid upload size/type.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var file = await db.StoredFiles.FromSqlInterpolated($"SELECT * FROM app.\"StoredFiles\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        if (file.Status == "Approved") return;
        file.Size = blob.Size; file.ETag = blob.ETag; file.VersionId = blob.VersionId; file.Status = "Uploaded";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ApproveAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var actor = await Actor(actorId, ct); if (actor.Role is not (ActorRole.Supervisor or ActorRole.Admin)) throw new PaymentConflictException("Supervisor required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var file = await db.StoredFiles.FromSqlInterpolated($"SELECT * FROM app.\"StoredFiles\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        if (file.Status == "Approved") return;
        if (file.Status != "Uploaded") throw new PaymentConflictException("Upload must be verified first.");
        file.Status = "Approved"; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<BlobGrant> ReadAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var actor = await Actor(actorId, ct); var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PaymentConflictException("File not found.");
        if (file.Status == "PendingUpload" || !await CanAccess(file.BookingId, file.Kind, actor, ct)) throw new PaymentConflictException("Not authorized.");
        var blob = await storage.InspectAsync(file.ObjectKey, ct);
        if (blob.ETag != file.ETag) throw new PaymentConflictException("Stored evidence changed; requires review.");
        return await storage.ReadAsync(file.ObjectKey, file.VersionId, ct);
    }
}
