using System.Security.Cryptography;
using System.Text.Json;
using Labaik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Labaik.Infrastructure.Payments;

public sealed class PaymentIngress(AppDbContext db, WebhookCodec codec)
{
    public async Task<bool> ReceiveAsync(byte[] body, string signature, CancellationToken ct = default)
    {
        var notice = codec.VerifyAndDecrypt(body, signature);
        var hash = Convert.ToHexString(SHA256.HashData(body)); var id = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."WebhookInbox" ("Id","Provider","EventId","PayloadHash","EncryptedBody","Signature","Status","HasConflict","ReceivedAt")
            VALUES ({id},'fake-hyperpay',{notice.EventId},{hash},{body},{signature},'Pending',false,clock_timestamp())
            ON CONFLICT ("Provider","EventId") DO NOTHING
            """, ct);
        var inbox = await db.WebhookInbox.FromSqlInterpolated($"""
            SELECT * FROM app."WebhookInbox" WHERE "Provider"='fake-hyperpay' AND "EventId"={notice.EventId} FOR UPDATE
            """).SingleAsync(ct);
        if (inserted == 1) WorkQueue.Enqueue(db, WorkKind.Inbox, inbox.Id, inbox.ReceivedAt);
        if (inbox.PayloadHash != hash)
        {
            inbox.HasConflict = true;
            if (inbox.Status != InboxStatus.Processed) inbox.Status = InboxStatus.Rejected;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return !inbox.HasConflict;
    }
}

public static class PaymentWebhookEndpoint
{
    // This signed callback adapter is mapped by test composition only in step 3. Production wiring waits for step 4.
    public static async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var signature = context.Request.Headers["X-Gateway-Signature"].ToString();
        if (signature.Length != 64) { context.Response.StatusCode = 401; return; }
        using var body = new MemoryStream(); var buffer = new byte[4096];
        int read;
        while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
        {
            if (body.Length + read > WebhookCodec.MaxBodyBytes) { context.Response.StatusCode = 413; return; }
            body.Write(buffer, 0, read);
        }
        try
        {
            var accepted = await context.RequestServices.GetRequiredService<PaymentIngress>().ReceiveAsync(body.ToArray(), signature, context.RequestAborted);
            context.Response.StatusCode = accepted ? 202 : 409;
        }
        catch (CryptographicException) { context.Response.StatusCode = 401; }
        catch (JsonException) { context.Response.StatusCode = 400; }
    }
}
