using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Labaik.Api;

public sealed record CreateBookingRequest(DateOnly ServiceDate, string? VoucherCode);
public sealed record CheckoutRequest(string RequestKey);
internal static class BookingEndpoints
{
    public static Guid Actor(HttpContext context) => Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
    public static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PaymentConflictException error) { return Results.Conflict(new { reason = error.Message }); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { reason = "Booking was changed; reload and retry." }); }
        catch (Exception error) when (error is PaymentDependencyException or NpgsqlException or TimeoutException or HttpRequestException or Application.Files.FileStoreUnavailableException or Azure.RequestFailedException)
        { return Results.StatusCode(503); }
    }
    public static void Map(WebApplication app)
    {
        app.MapPost("/bookings", (CreateBookingRequest request, HttpContext context, BookingService service) => Run(async () =>
            Results.Ok(await service.CreateAsync(Actor(context), request.ServiceDate, request.VoucherCode, context.RequestAborted)))).RequireAuthorization();
        app.MapGet("/bookings/{id:guid}", (Guid id, HttpContext context, AppDbContext db) => Run(async () =>
        {
            var actor = await db.Users.SingleOrDefaultAsync(x => x.Id == Actor(context));
            var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (booking is null || actor is null || actor.Status != Domain.Users.UserStatus.Active) return Results.NotFound();
            return booking.CustomerId == actor.Id || actor.Role is ActorRole.Admin or ActorRole.Supervisor or ActorRole.Accountant || await db.PerformerOffers.AnyAsync(x => x.BookingId == id && x.PerformerId == actor.Id && (x.Status == "Pending" || x.Status == "Accepted" || x.Status == "Finished")) ? Results.Ok(booking) : Results.NotFound();
        })).RequireAuthorization();
        app.MapPost("/bookings/{id:guid}/checkout", (Guid id, CheckoutRequest request, HttpContext context) => Run(async () =>
        {
            var service = context.RequestServices.GetRequiredService<PaymentCheckout>();
            return Results.Ok(await service.CheckoutAsync(id, Actor(context), request.RequestKey, context.RequestAborted));
        })).RequireAuthorization();
        app.MapPost("/bookings/{id:guid}/cancel", (Guid id, HttpContext context, BookingMoney service) => Run(async () =>
        { await service.CancelAsync(id, Actor(context), context.RequestAborted); return Results.NoContent(); })).RequireAuthorization();
        app.MapPost("/webhooks/gateway", PaymentWebhookEndpoint.HandleAsync).AllowAnonymous();
        app.MapGet("/payments/return", (Guid checkoutId, HttpContext context, AppDbContext db) => Run(async () =>
        {
            var result = await (from attempt in db.PaymentAttempts from booking in db.Bookings
                where attempt.CheckoutId == checkoutId && attempt.BookingId == booking.Id && booking.CustomerId == Actor(context)
                select new { attempt.Id, attempt.Status, BookingState = booking.State }).SingleOrDefaultAsync();
            return result is null ? Results.NotFound() : Results.Ok(result); // Redirect is read-only; only verified workers mark paid.
        })).RequireAuthorization();
    }
}
