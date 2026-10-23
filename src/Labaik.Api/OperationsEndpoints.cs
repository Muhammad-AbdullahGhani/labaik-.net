using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Api;

public sealed record FileUploadRequest(Guid BookingId, string Kind);
public sealed record BookingMoveRequest(Labaik.Domain.Bookings.BookingState Target);
public sealed record PayoutCreateRequest(Guid PerformerId, DateOnly WeekStart, string RequestKey);
public sealed record PayoutResultRequest(string Status, string Reference);
internal static class OperationsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/bookings/{id:guid}/transition", (Guid id, BookingMoveRequest request, HttpContext context, CompletionService service) => BookingEndpoints.Run(async () =>
        { await service.MoveAsync(id, BookingEndpoints.Actor(context), request.Target, context.RequestAborted); return Results.NoContent(); })).RequireAuthorization();
        app.MapPost("/payouts", (PayoutCreateRequest request, HttpContext context, PayoutService service) => BookingEndpoints.Run(async () =>
            Results.Ok(await service.CreateAsync(request.PerformerId, request.WeekStart, request.RequestKey, BookingEndpoints.Actor(context), context.RequestAborted)))).RequireAuthorization();
        app.MapPost("/payouts/{id:guid}/result", (Guid id, PayoutResultRequest request, HttpContext context, PayoutService service) => BookingEndpoints.Run(async () =>
        { await service.ConfirmAsync(id, request.Status, request.Reference, BookingEndpoints.Actor(context), context.RequestAborted); return Results.NoContent(); })).RequireAuthorization();
        app.MapGet("/payouts/{id:guid}/export", (Guid id, HttpContext context, PayoutService service) => BookingEndpoints.Run(async () =>
        { context.Response.Headers.CacheControl = "no-store"; return Results.File(await service.ExportAsync(id, BookingEndpoints.Actor(context), context.RequestAborted), "text/csv", $"payout-{id:D}.csv"); })).RequireAuthorization();
        app.MapGet("/offers/mine", async (HttpContext context, AppDbContext db) =>
            Results.Ok(await db.PerformerOffers.AsNoTracking().Where(x => x.PerformerId == BookingEndpoints.Actor(context) && (x.Status == "Pending" || x.Status == "Accepted")).ToListAsync())).RequireAuthorization();
        app.MapPost("/offers/{id:guid}/accept", (Guid id, HttpContext context, AssignmentService service) => BookingEndpoints.Run(async () =>
            Results.Ok(await service.AcceptAsync(id, BookingEndpoints.Actor(context), context.RequestAborted)))).RequireAuthorization();
        app.MapPost("/performers/{id:guid}/suspend", (Guid id, HttpContext context, AssignmentService service) => BookingEndpoints.Run(async () =>
        { await service.SuspendAsync(id, BookingEndpoints.Actor(context), context.RequestAborted); return Results.NoContent(); })).RequireAuthorization();
        app.MapPost("/files/upload", (FileUploadRequest request, HttpContext context) => BookingEndpoints.Run(async () =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await context.RequestServices.GetRequiredService<PrivateFileService>().UploadAsync(request.BookingId, BookingEndpoints.Actor(context), request.Kind, context.RequestAborted));
        })).RequireAuthorization();
        app.MapPost("/files/{id:guid}/complete", (Guid id, HttpContext context) => BookingEndpoints.Run(async () =>
        { await context.RequestServices.GetRequiredService<PrivateFileService>().CompleteAsync(id, BookingEndpoints.Actor(context), context.RequestAborted); return Results.NoContent(); })).RequireAuthorization();
        app.MapPost("/files/{id:guid}/approve", (Guid id, HttpContext context) => BookingEndpoints.Run(async () =>
        { await context.RequestServices.GetRequiredService<PrivateFileService>().ApproveAsync(id, BookingEndpoints.Actor(context), context.RequestAborted); return Results.NoContent(); })).RequireAuthorization();
        app.MapGet("/files/{id:guid}/read", (Guid id, HttpContext context) => BookingEndpoints.Run(async () =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await context.RequestServices.GetRequiredService<PrivateFileService>().ReadAsync(id, BookingEndpoints.Actor(context), context.RequestAborted));
        })).RequireAuthorization();
    }
}
