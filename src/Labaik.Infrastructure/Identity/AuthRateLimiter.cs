using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Identity;

public sealed class AuthRateLimiter(AppDbContext db, TokenCodec codec, AuthSettings settings)
{
    public Task<bool> IpAsync(string ip, CancellationToken ct) => TakeAsync("ip:" + ip, settings.IpLimit, ct);
    public Task<bool> AccountAsync(string project, string uid, CancellationToken ct) => TakeAsync("account:" + project + ":" + uid, settings.AccountLimit, ct);
    private async Task<bool> TakeAsync(string subject, int limit, CancellationToken ct)
    {
        var key = codec.RateKey(subject);
        // One atomic PostgreSQL upsert gives all API instances the same fixed-minute budget.
        // Limits are consumed outside the rotation transaction, so denied/replayed requests cannot roll them back.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."AuthRateBuckets" ("Key","WindowStart","Count") VALUES ({key},clock_timestamp(),1)
            ON CONFLICT ("Key") DO UPDATE SET
              "Count"=CASE WHEN "AuthRateBuckets"."WindowStart" <= clock_timestamp()-interval '1 minute' THEN 1 ELSE "AuthRateBuckets"."Count"+1 END,
              "WindowStart"=CASE WHEN "AuthRateBuckets"."WindowStart" <= clock_timestamp()-interval '1 minute' THEN clock_timestamp() ELSE "AuthRateBuckets"."WindowStart" END
            WHERE "AuthRateBuckets"."WindowStart" <= clock_timestamp()-interval '1 minute' OR "AuthRateBuckets"."Count" < {limit}
            """, ct);
        return rows == 1;
    }
}
