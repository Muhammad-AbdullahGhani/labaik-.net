using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Payments;

public sealed class WorkPolicy
{
    public int LeaseSeconds { get; init; } = 30;
    public int MaxAttempts { get; init; } = 5;
}
public sealed record WorkClaim(Guid Id, Guid Owner, long Version, WorkKind Kind, Guid EntityId);
public sealed class WorkQueue(AppDbContext db, WorkPolicy policy)
{
    public static void Enqueue(AppDbContext db, WorkKind kind, Guid entityId, DateTimeOffset now, string? suffix = null)
    {
        // Immediate work is scheduled once per entity. Retries change AvailableAt, not the unique schedule key.
        db.JobRuns.Add(new JobRun { Id = Guid.NewGuid(), JobName = $"{kind}:{entityId:N}" + (suffix is null ? "" : ":"+suffix), ScheduledFor = DateTimeOffset.UnixEpoch,
            Kind = kind, EntityId = entityId, AvailableAt = now });
    }
    public Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    public async Task<WorkClaim?> ClaimAsync(WorkKind? kind, CancellationToken ct)
    {
        var owner = Guid.NewGuid(); var filter = kind?.ToString() ?? "";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE app."JobRuns" SET "Status"='DeadLetter',"LeaseOwner"=NULL,"LeaseUntil"=NULL,"LastError"='AttemptsExhausted'
            WHERE "Attempts">={policy.MaxAttempts} AND ("Status"='Pending' OR ("Status"='Processing' AND "LeaseUntil"<=clock_timestamp()))
            """, ct);
        var rows = await db.JobRuns.FromSqlInterpolated($"""
            UPDATE app."JobRuns" AS job SET "Status"='Processing',"LeaseOwner"={owner},
              "LeaseUntil"=clock_timestamp()+({policy.LeaseSeconds} * interval '1 second'),
              "ClaimVersion"=job."ClaimVersion"+1,"Attempts"=job."Attempts"+1
            FROM (SELECT "Id" FROM app."JobRuns"
              WHERE ("Status"='Pending' AND "AvailableAt"<=clock_timestamp() OR "Status"='Processing' AND "LeaseUntil"<=clock_timestamp())
                AND "Attempts"<{policy.MaxAttempts} AND ({filter}='' OR "Kind"={filter})
              ORDER BY "AvailableAt","Id" FOR UPDATE SKIP LOCKED LIMIT 1) AS candidate
            WHERE job."Id"=candidate."Id" RETURNING job.*
            """).AsNoTracking().ToListAsync(ct);
        var row = rows.SingleOrDefault();
        return row is null ? null : new(row.Id, owner, row.ClaimVersion, row.Kind, row.EntityId);
    }
    public Task<JobRun?> LockAsync(WorkClaim claim, CancellationToken ct) => db.JobRuns.FromSqlInterpolated($"""
        SELECT * FROM app."JobRuns" WHERE "Id"={claim.Id} AND "Status"='Processing' AND "LeaseOwner"={claim.Owner}
          AND "ClaimVersion"={claim.Version} AND "LeaseUntil">clock_timestamp() FOR UPDATE
        """).SingleOrDefaultAsync(ct);
    public static void Complete(JobRun job)
    { job.Status = WorkStatus.Completed; job.LeaseOwner = null; job.LeaseUntil = null; job.LastError = null; }
    public void Retry(JobRun job, DateTimeOffset now, string reason, bool failure)
    {
        job.LastError = reason; job.LeaseOwner = null; job.LeaseUntil = null;
        if (!failure) job.Attempts = 0;
        job.Status = job.Attempts >= policy.MaxAttempts ? WorkStatus.DeadLetter : WorkStatus.Pending;
        job.AvailableAt = now.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(job.Attempts, 6))));
    }
    public async Task FailAsync(WorkClaim claim, CancellationToken ct, string errorType)
    {
        // A failed business transaction leaves tracked inserts behind; never accidentally save them with the retry.
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await LockAsync(claim, ct);
        if (job is null) return;
        Retry(job, await Now(ct), errorType, true);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
