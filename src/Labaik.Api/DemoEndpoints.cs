using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Api;

public sealed record DemoPaymentRequest(string Outcome);

internal static class DemoEndpoints
{
    // Two explicit gates keep operator simulation routes and the local UI out of normal production startup.
    public static bool Enabled(IConfiguration config, IHostEnvironment environment) =>
        environment.IsEnvironment("Demo") && config.GetValue("Demo:Enabled", false);

    public static void Add(WebApplicationBuilder builder)
    {
        if (!Enabled(builder.Configuration, builder.Environment)) return;
        builder.Services.AddHttpClient("DemoGateway", client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Payments:GatewayUrl"] ?? throw new InvalidOperationException("Configure the fake gateway."));
            client.DefaultRequestHeaders.Add("X-Gateway-Key", builder.Configuration["Gateway:ApiKey"] ?? throw new InvalidOperationException("Configure the private fake-gateway key."));
            client.Timeout = TimeSpan.FromSeconds(5);
        });
    }

    private static Task<User?> Actor(HttpContext context, AppDbContext db) => db.Users.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == BookingEndpoints.Actor(context) && x.Status == UserStatus.Active, context.RequestAborted);

    public static void Map(WebApplication app)
    {
        if (!Enabled(app.Configuration, app.Environment)) return;
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' https://www.gstatic.com; connect-src 'self' https://*.googleapis.com https://*.firebaseapp.com; frame-src https://*.firebaseapp.com; worker-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapGet("/", () => Results.Redirect("/demo/"));
        app.MapGet("/demo/config.js", () =>
        {
            // Public browser configuration only. Admin credentials, gateway keys and email settings never leave the server.
            var config = app.Configuration;
            return Results.Text("globalThis.LABAIK_DEMO = " + JsonSerializer.Serialize(new
            {
                firebase = new { apiKey = config["Demo:WebApiKey"], projectId = config["Firebase:ProjectId"],
                    authDomain = config["Firebase:ProjectId"] + ".firebaseapp.com", messagingSenderId = config["Demo:MessagingSenderId"], appId = config["Demo:WebAppId"] },
                vapidKey = config["Demo:VapidPublicKey"]
            }) + ";", "application/javascript");
        });
        app.MapGet("/demo/bookings", (HttpContext context, AppDbContext db) => BookingEndpoints.Run(async () =>
        {
            var actor = await Actor(context, db); if (actor is null) return Results.Forbid();
            var staff = actor.Role is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Accountant;
            var query = db.Bookings.AsNoTracking().AsQueryable();
            if (!staff) query = query.Where(x => x.CustomerId == actor.Id);
            var bookings = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(50).ToListAsync(context.RequestAborted);
            var ids = bookings.Select(x => x.Id).ToArray();
            var attempts = await db.PaymentAttempts.AsNoTracking().Where(x => ids.Contains(x.BookingId)).ToListAsync(context.RequestAborted);
            var captures = await db.CapturedPayments.AsNoTracking().Where(x => ids.Contains(x.BookingId)).ToListAsync(context.RequestAborted);
            var captureIds = captures.Select(x => x.Id).ToArray();
            var refunds = await db.RefundRequests.AsNoTracking().Where(x => captureIds.Contains(x.CapturedPaymentId)).ToListAsync(context.RequestAborted);
            return Results.Ok(bookings.Select(booking =>
            {
                var attempt = attempts.Where(x => x.BookingId == booking.Id).OrderByDescending(x => x.Sequence).FirstOrDefault();
                var capture = captures.SingleOrDefault(x => x.BookingId == booking.Id);
                return new { booking.Id, booking.ServiceDate, state = booking.State.ToString(), booking.Version,
                    totalMinor = booking.TotalMinor.ToString(CultureInfo.InvariantCulture), booking.Currency, booking.CreatedAt,
                    owned = booking.CustomerId == actor.Id, attemptId = attempt?.Id, checkoutId = attempt?.CheckoutId,
                    paymentStatus = attempt?.Status.ToString(), captured = capture is not null,
                    refundStatus = refunds.SingleOrDefault(x => x.CapturedPaymentId == capture?.Id)?.Status };
            }));
        })).RequireAuthorization();
        app.MapPost("/demo/bookings/{id:guid}/pay", (Guid id, DemoPaymentRequest request, HttpContext context, AppDbContext db, IHttpClientFactory clients) => BookingEndpoints.Run(async () =>
        {
            var actor = await Actor(context, db); if (actor is null || actor.Role is not (ActorRole.Customer or ActorRole.Admin)) return Results.Forbid();
            if (request.Outcome is not ("Succeeded" or "Failed" or "Pending")) return Results.BadRequest();
            var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == actor.Id, context.RequestAborted);
            if (booking is null) return Results.NotFound();
            var attempt = await db.PaymentAttempts.AsNoTracking().Where(x => x.BookingId == id).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(context.RequestAborted);
            if (attempt?.CheckoutId is null) return Results.Conflict(new { reason = "Create or recover this booking's checkout first." });
            // Only the fake provider changes here. Existing signed-callback/re-query workers alone apply capture/state/outbox.
            using var client = clients.CreateClient("DemoGateway");
            try
            {
                using var response = await client.PostAsJsonAsync($"checkouts/{attempt.CheckoutId:D}/pay", new { request.Outcome, Redirect = false }, context.RequestAborted);
                if (!response.IsSuccessStatusCode) return Results.StatusCode(503);
                return Results.Accepted(value: new { status = "Submitted", message = "Fake gateway updated. Await verified booking status." });
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { return Results.StatusCode(503); } // A local timeout says nothing about the provider outcome; keep the same attempt.
        })).RequireAuthorization();
        app.MapGet("/demo/payments", (HttpContext context, AppDbContext db) => BookingEndpoints.Run(async () =>
        {
            var actor = await Actor(context, db);
            if (actor?.Role is not (ActorRole.Admin or ActorRole.Accountant)) return Results.Forbid();
            var captures = await db.CapturedPayments.AsNoTracking().OrderByDescending(x => x.CapturedAt).Take(50).ToListAsync(context.RequestAborted);
            var ids = captures.Select(x => x.Id).ToArray();
            var refunds = await db.RefundRequests.AsNoTracking().Where(x => ids.Contains(x.CapturedPaymentId)).ToListAsync(context.RequestAborted);
            return Results.Ok(captures.Select(x => new { x.Id, x.BookingId, x.CapturedAt,
                amountMinor = x.AmountMinor.ToString(CultureInfo.InvariantCulture), x.Currency,
                refundStatus = refunds.SingleOrDefault(r => r.CapturedPaymentId == x.Id)?.Status }));
        })).RequireAuthorization();
        app.MapGet("/demo/jobs", (HttpContext context, AppDbContext db) => BookingEndpoints.Run(async () =>
        {
            var actor = await Actor(context, db);
            if (actor?.Role is not (ActorRole.Admin or ActorRole.Accountant)) return Results.Forbid();
            var counts = await db.JobRuns.GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(context.RequestAborted);
            var heartbeat = await db.JobRuns.Where(x => x.Kind == Labaik.Infrastructure.Payments.WorkKind.Heartbeat).OrderByDescending(x => x.AvailableAt).Select(x => (DateTimeOffset?)x.AvailableAt).FirstOrDefaultAsync(context.RequestAborted);
            return Results.Ok(new { counts = counts.Select(x => new { status = x.Status.ToString(), x.Count }), latestHeartbeatAt = heartbeat });
        })).RequireAuthorization();
    }
}
