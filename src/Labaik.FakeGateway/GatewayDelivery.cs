using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Labaik.Infrastructure.Payments;

namespace Labaik.FakeGateway;

public sealed class GatewayDelivery(GatewayDbContext db, HttpClient client, WebhookCodec codec, GatewaySimulation simulation)
{
    public async Task<bool> RunOnceAsync(CancellationToken ct = default)
    {
        var owner = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("UPDATE fake_gateway.\"Events\" SET \"Status\"='DeadLetter',\"LeaseOwner\"=NULL,\"LeaseUntil\"=NULL WHERE \"Attempts\">=5 AND (\"Status\"='Pending' OR (\"Status\"='Processing' AND \"LeaseUntil\"<=clock_timestamp()))", ct);
        var claimed = await db.Events.FromSqlInterpolated($"""
            UPDATE fake_gateway."Events" AS event SET "Status"='Processing',"LeaseOwner"={owner},
                "LeaseUntil"=clock_timestamp()+interval '30 seconds',"ClaimVersion"=event."ClaimVersion"+1,"Attempts"=event."Attempts"+1
            FROM (SELECT "Id" FROM fake_gateway."Events" WHERE "Attempts"<5 AND
                ("Status"='Pending' AND "AvailableAt"<=clock_timestamp() OR "Status"='Processing' AND "LeaseUntil"<=clock_timestamp())
                ORDER BY "AvailableAt","Id" FOR UPDATE SKIP LOCKED LIMIT 1) AS candidate
            WHERE event."Id"=candidate."Id" RETURNING event.*
            """).AsNoTracking().ToListAsync(ct);
        var item = claimed.SingleOrDefault(); if (item is null) return false;
        var success = true;
        try
        {
            for (var copy = 0; copy < Math.Clamp(simulation.DeliveryCopies, 1, 3); copy++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = new ByteArrayContent(item.Body) };
                request.Headers.Add("X-Gateway-Signature", codec.Sign(item.Body));
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) { success = false; break; }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { success = false; }
        // A lost acknowledgement retries the same persisted event, never a newly encrypted copy.
        var status = success ? "Completed" : item.Attempts >= 5 ? "DeadLetter" : "Pending";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE fake_gateway."Events" SET "Status"={status},"LeaseOwner"=NULL,"LeaseUntil"=NULL,
              "AvailableAt"=clock_timestamp()+(POWER(2,LEAST("Attempts",6))*interval '1 second')
            WHERE "Id"={item.Id} AND "LeaseOwner"={owner} AND "ClaimVersion"={item.ClaimVersion} AND "LeaseUntil">clock_timestamp()
            """, ct);
        return true;
    }
}


// Disabled by default until step 4 maps the production callback. Tests drive one durable iteration directly.
public sealed class GatewayDeliveryWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<GatewayDeliveryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Gateway:DispatchEnabled", false)) return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var delivery = scope.ServiceProvider.GetRequiredService<GatewayDelivery>();
                    if (!await delivery.RunOnceAsync(stoppingToken)) await Task.Delay(250, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogWarning("Gateway delivery unavailable; durable events will retry"); await Task.Delay(1000, stoppingToken); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
