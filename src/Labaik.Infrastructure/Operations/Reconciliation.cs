using System.Text.Json;
using Labaik.Application.Payments;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Operations;

public sealed record ReconciliationMismatch(string Kind, Guid TransactionId, Guid BookingId);
public sealed class ReconciliationService(AppDbContext db, WorkQueue queue, IPaymentGateway gateway)
{
    public async Task HandleAsync(WorkClaim claim, CancellationToken ct)
    {
        var remote = new Dictionary<Guid,GatewayLedgerRow>(); Guid? cursor=null;
        for (var page=0; ;page++)
        {
            if (page>=100) throw new PaymentDependencyException(); // Bound memory and visibly fail rather than publish an incomplete clean report.
            var result=await gateway.ListAsync(cursor,ct);
            foreach(var row in result.Rows) remote[row.TransactionId]=row;
            if(result.Next is null) break;
            if(result.Next==cursor) throw new PaymentDependencyException();
            cursor=result.Next;
        }
        var local=await db.CapturedPayments.AsNoTracking().ToDictionaryAsync(x=>x.GatewayTransactionId,ct);
        var refunds=await db.RefundRequests.AsNoTracking().Where(x=>x.Status=="Succeeded").ToListAsync(ct);
        var mismatches=new List<ReconciliationMismatch>();
        foreach(var row in remote.Values)
        {
            if(!local.TryGetValue(row.TransactionId,out var capture)) mismatches.Add(new("MissingLocalCapture",row.TransactionId,row.BookingId));
            else if(capture.BookingId!=row.BookingId || capture.AmountMinor!=row.AmountMinor || capture.Currency!=row.Currency) mismatches.Add(new("CaptureMismatch",row.TransactionId,row.BookingId));
            if(row.RefundId is not null && !refunds.Any(x=>x.CapturedPaymentId==capture?.Id && x.GatewayRefundId==row.RefundId)) mismatches.Add(new("MissingLocalRefund",row.TransactionId,row.BookingId));
        }
        foreach(var capture in local.Values)
        {
            if(!remote.ContainsKey(capture.GatewayTransactionId)) mismatches.Add(new("MissingGatewayCapture",capture.GatewayTransactionId,capture.BookingId));
            foreach(var refund in refunds.Where(x=>x.CapturedPaymentId==capture.Id))
                if(!remote.TryGetValue(capture.GatewayTransactionId,out var row) || row.RefundId!=refund.GatewayRefundId) mismatches.Add(new("MissingGatewayRefund",capture.GatewayTransactionId,capture.BookingId));
        }
        await using var tx=await db.Database.BeginTransactionAsync(ct); var job=await queue.LockAsync(claim,ct); if(job is null)return;
        job.ResultJson=JsonSerializer.Serialize(new {CheckedAt=await queue.Now(ct),GatewayCount=remote.Count,LocalCount=local.Count,MismatchCount=mismatches.Count,Truncated=mismatches.Count>1000,Mismatches=mismatches.Take(1000)});
        // A report does not assert capture or alter money. Immediate recovery jobs remain the authoritative repair path.
        WorkQueue.Complete(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
public sealed class WorkerIdentity
{
    public Guid Id { get; }=Guid.NewGuid();
    public string Name => $"worker-heartbeat:{Id:N}";
}
public sealed class OperationsScheduler(AppDbContext db, WorkQueue queue, WorkerIdentity worker)
{
    public async Task TickAsync(CancellationToken ct)
    {
        var now=await queue.Now(ct); var day=new DateTimeOffset(now.UtcDateTime.Date,TimeSpan.Zero); var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."JobRuns" ("Id","JobName","ScheduledFor","Kind","EntityId","Status","Attempts","AvailableAt","ClaimVersion")
            VALUES ({id},'nightly-reconciliation',{day},'Reconcile',{id},'Pending',0,{now},0)
            ON CONFLICT ("JobName","ScheduledFor") DO NOTHING
            """,ct);
        var monday=day.AddDays(-(((int)day.DayOfWeek+6)%7)); var weeklyId=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."JobRuns" ("Id","JobName","ScheduledFor","Kind","EntityId","Status","Attempts","AvailableAt","ClaimVersion")
            VALUES ({weeklyId},'weekly-payouts',{monday},'PayoutSweep',{weeklyId},'Pending',0,{now},0)
            ON CONFLICT ("JobName","ScheduledFor") DO NOTHING
            """,ct);
        var epoch=DateTimeOffset.UnixEpoch;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."JobRuns" ("Id","JobName","ScheduledFor","Kind","EntityId","Status","Attempts","AvailableAt","ClaimVersion")
            VALUES ({worker.Id},{worker.Name},{epoch},'Heartbeat',{worker.Id},'Completed',0,{now},0)
            ON CONFLICT ("JobName","ScheduledFor") DO UPDATE SET "AvailableAt"=EXCLUDED."AvailableAt"
            """,ct);
    }
}
public sealed class WeeklyPayouts(AppDbContext db, WorkQueue queue, PayoutService payouts)
{
    public async Task HandleAsync(WorkClaim claim,CancellationToken ct)
    {
        var run=await db.JobRuns.AsNoTracking().SingleAsync(x=>x.Id==claim.Id,ct);
        var week=DateOnly.FromDateTime(run.ScheduledFor.UtcDateTime).AddDays(-7);
        var performers=await db.Earnings.AsNoTracking().Where(x=>x.Status=="Available").Select(x=>x.PerformerId).Distinct().ToListAsync(ct);
        var batches=new List<Guid>();
        if(performers.Count>0)
        {
            var accountant=await db.Users.Where(x=>x.Status==Labaik.Domain.Users.UserStatus.Active && (x.Role==Labaik.Domain.Bookings.ActorRole.Accountant || x.Role==Labaik.Domain.Bookings.ActorRole.Admin)).OrderBy(x=>x.Id).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct)
                ?? throw new PaymentConflictException("Provision an accountant before scheduled payout claims.");
            foreach(var performer in performers)
            {
                var batch=await payouts.CreateAsync(performer,week,$"weekly:{week:yyyyMMdd}:{performer:N}",accountant,ct);
                if(batch is not null)batches.Add(batch.Id);
            }
        }
        await using var tx=await db.Database.BeginTransactionAsync(ct);var job=await queue.LockAsync(claim,ct);if(job is null)return;
        job.ResultJson=JsonSerializer.Serialize(new{Week=week,Batches=batches});WorkQueue.Complete(job);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
