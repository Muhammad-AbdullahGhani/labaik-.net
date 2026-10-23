using Labaik.Application.Files;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Labaik.Infrastructure.Operations;

public sealed class BlobReadiness(IPrivateBlobStore storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken cancellationToken=default)
    {
        try {return await storage.ReadyAsync(cancellationToken)?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("Private storage unavailable");}
        catch(Exception) {return HealthCheckResult.Unhealthy("Private storage unavailable");}
    }
}
public sealed class WorkerReadiness(AppDbContext db,WorkerIdentity worker) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken cancellationToken=default)
    {
        try
        {
            var healthy=await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM app.\"JobRuns\" WHERE \"JobName\"={worker.Name} AND \"AvailableAt\">clock_timestamp()-interval '30 seconds') AS \"Value\"").SingleAsync(cancellationToken);
            return healthy?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("Worker heartbeat stale");
        }
        catch(Exception) {return HealthCheckResult.Unhealthy("Worker heartbeat unavailable");}
    }
}
