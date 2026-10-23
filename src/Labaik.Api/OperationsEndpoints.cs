using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Api;

public sealed record FileUploadRequest(Guid BookingId, string Kind);
public sealed record BookingMoveRequest(Labaik.Domain.Bookings.BookingState Target);
public sealed record PayoutCreateRequest(Guid PerformerId, DateOnly WeekStart, string RequestKey);
public sealed record PayoutResultRequest(string Status, string Reference);
public sealed record DeviceRequest(string Token, string Locale);
internal static class OperationsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/operations", (HttpContext context, AppDbContext db) => BookingEndpoints.Run(async () =>
        {
            var actor=BookingEndpoints.Actor(context);
            if(!await db.Users.AnyAsync(x=>x.Id==actor && x.Status==Labaik.Domain.Users.UserStatus.Active && (x.Role==Labaik.Domain.Bookings.ActorRole.Admin || x.Role==Labaik.Domain.Bookings.ActorRole.Accountant))) return Results.Forbid();
            return Results.Ok(new {
                Reports=await db.JobRuns.AsNoTracking().Where(x=>x.Kind==Labaik.Infrastructure.Payments.WorkKind.Reconcile).OrderByDescending(x=>x.ScheduledFor).Take(7).Select(x=>new {x.Id,x.Status,x.ScheduledFor,x.ResultJson,x.LastError}).ToListAsync(),
                DeadLetters=await db.JobRuns.AsNoTracking().Where(x=>x.Status==Labaik.Infrastructure.Payments.WorkStatus.DeadLetter).Select(x=>new {x.Id,x.Kind,x.EntityId,x.Attempts,x.LastError}).Take(100).ToListAsync(),
                Alerts=await db.OutboxMessages.AsNoTracking().Where(x=>x.Type=="PaymentMismatch" || x.Type=="AccountantAlert").OrderByDescending(x=>x.Id).Select(x=>new {x.Id,x.BookingId,x.Type,x.Payload}).Take(100).ToListAsync()
            });
        })).RequireAuthorization();
        app.MapPost("/notifications/device", (DeviceRequest request, HttpContext context, AppDbContext db) => BookingEndpoints.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length>4096 || request.Locale is not ("ar" or "en")) return Results.BadRequest();
            var actor=BookingEndpoints.Actor(context);
            var user=await db.Users.SingleAsync(x=>x.Id==actor && x.Status==Labaik.Domain.Users.UserStatus.Active);
            db.Entry(user).Property(x=>x.Locale).CurrentValue=request.Locale;
            // A device token is an opaque delivery credential; it cannot grant a role or link an identity.
            var existing=await db.DeviceTokens.SingleOrDefaultAsync(x=>x.Token==request.Token);
            if(existing is null) db.DeviceTokens.Add(new DeviceToken {Id=Guid.NewGuid(),UserId=actor,Token=request.Token});
            else {existing.UserId=actor;existing.InvalidatedAt=null;}
            await db.SaveChangesAsync(context.RequestAborted); return Results.NoContent();
        })).RequireAuthorization();
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
