using System.Security.Cryptography;
using System.Text;
using Labaik.Application.Payments;
using Labaik.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace Labaik.FakeGateway;

public sealed record SimulatedPayment(string Outcome, bool Redirect = false);
internal static class GatewayEndpoints
{
    public static void Add(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32768);
        builder.Services.AddSingleton(_ => new WebhookCodec(
            Convert.FromBase64String(builder.Configuration["Payments:WebhookEncryptionKey"] ?? ""),
            Convert.FromBase64String(builder.Configuration["Payments:WebhookSignatureKey"] ?? "")));
        builder.Services.AddSingleton(builder.Configuration.GetSection("Gateway:Simulation").Get<GatewaySimulation>() ?? new());
        builder.Services.AddScoped<GatewayService>();
        builder.Services.AddHttpClient<GatewayDelivery>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Gateway:WebhookUrl"] ?? throw new PaymentDependencyException());
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        builder.Services.AddHostedService<GatewayDeliveryWorker>();
    }
    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if ((context.Request.Path.StartsWithSegments("/checkouts") || context.Request.Path.StartsWithSegments("/refunds") || context.Request.Path.StartsWithSegments("/transactions")))
            {
                var expected = app.Configuration["Gateway:ApiKey"];
                if (expected is not { Length: >= 32 }) { context.Response.StatusCode = 503; return; }
                var supplied = context.Request.Headers["X-Gateway-Key"].ToString();
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
                { context.Response.StatusCode = 401; return; }
            }
            await next(context);
        });
        app.MapGet("/transactions", async (Guid? after, GatewayDbContext db, HttpContext context) =>
        {
            var rows=await (from charge in db.Charges.AsNoTracking() join refund in db.Refunds on charge.Id equals refund.TransactionId into refunds from refund in refunds.DefaultIfEmpty()
                where after==null || charge.Id.CompareTo(after.Value)>0 orderby charge.Id
                select new GatewayLedgerRow(charge.CheckoutId,charge.BookingId,charge.Id,charge.AmountMinor,charge.Currency,refund==null?null:refund.Id)).Take(250).ToArrayAsync(context.RequestAborted);
            return Results.Ok(new GatewayLedgerPage(rows,rows.Length==250?rows[^1].TransactionId:null));
        });
        app.MapPost("/refunds", async (RefundCommand command, GatewayService service, GatewaySimulation faults, HttpContext context) =>
            await Handle(async () => { var result = await service.RefundAsync(command, context.RequestAborted); return faults.FailRefundResponse ? Results.StatusCode(503) : Results.Ok(result); }));
        app.MapPost("/checkouts", async (CheckoutCommand command, GatewayService service, GatewaySimulation faults, HttpContext context) =>
            await Handle(async () =>
            {
                var result = await service.CreateAsync(command, context.RequestAborted);
                await Task.Delay(Math.Clamp(faults.CreateDelayMs, 0, 30000), context.RequestAborted);
                return faults.FailCreateResponse ? Results.StatusCode(503) : Results.Ok(result);
            }));
        app.MapGet("/checkouts/{id:guid}", async (Guid id, GatewayDbContext db, GatewaySimulation faults, HttpContext context) =>
            await Handle(async () =>
            {
                await Task.Delay(Math.Clamp(faults.QueryDelayMs, 0, 30000), context.RequestAborted);
                if (faults.QueryUnavailable) return Results.StatusCode(503);
                var checkout = await db.Checkouts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.RequestAborted);
                return checkout is null ? Results.NotFound() : Results.Ok(GatewayService.Snapshot(checkout));
            }));
        app.MapPost("/checkouts/{id:guid}/pay", async (Guid id, SimulatedPayment payment, GatewayService service, GatewaySimulation faults, HttpContext context) =>
            await Handle(async () =>
            {
                var returnUrl = context.RequestServices.GetRequiredService<IConfiguration>()["Gateway:ReturnUrl"];
                if (payment.Redirect && (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var destination) ||
                    destination.UserInfo.Length != 0 || !(destination.Scheme == "https" || destination.Scheme == "http" && destination.IsLoopback)))
                    return Results.StatusCode(503);
                var result = await service.PayAsync(id, payment.Outcome, context.RequestAborted);
                // Failure/delay happens AFTER commit, modeling a lost response after a charge.
                await Task.Delay(Math.Clamp(faults.PayResponseDelayMs, 0, 30000), context.RequestAborted);
                if (faults.FailPayResponse) return Results.StatusCode(503);
                return payment.Redirect
                    ? Results.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(returnUrl!, "checkoutId", id.ToString()))
                    : Results.Ok(result);
            }));
    }
    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ArgumentException) { return Results.BadRequest(); }
        catch (PaymentConflictException) { return Results.Conflict(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }
}
