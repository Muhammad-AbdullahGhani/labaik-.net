using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Labaik.Infrastructure.Identity;

public sealed class AuthCleanupWorker(IServiceScopeFactory scopes, ILogger<AuthCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    // Idempotent cleanup can run on every instance; credentials remain hashed for replay detection.
                    await db.Database.ExecuteSqlRawAsync("UPDATE app.\"RefreshTokens\" SET \"EncryptedSuccessor\"=NULL WHERE \"RetryUntil\" <= clock_timestamp() AND \"EncryptedSuccessor\" IS NOT NULL", stoppingToken);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM app.\"AuthRateBuckets\" WHERE \"WindowStart\" < clock_timestamp()-interval '10 minutes'", stoppingToken);
                }
                catch (Exception error) when (error is NpgsqlException or TimeoutException)
                { logger.LogWarning("Identity cleanup unavailable; will retry"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
