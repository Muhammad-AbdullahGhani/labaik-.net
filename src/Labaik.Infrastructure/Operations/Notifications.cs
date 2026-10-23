using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Labaik.Infrastructure.Operations;

public sealed class DeviceToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = null!;
    public DateTimeOffset? InvalidatedAt { get; set; }
}
public sealed record Delivery(Guid UserId, string Channel, string EventType, Guid? DeviceId);
public sealed record NotificationText(string Subject, string Body);
public static class NotificationTemplates
{
    public static NotificationText Render(string type, string locale) => (type, locale == "ar") switch
    {
        ("BookingPaid",false) => new("Payment confirmed", "Your payment is confirmed. We are finding a performer."),
        ("BookingPaid",true) => new("تم تأكيد الدفع", "تم تأكيد دفعتك. نبحث الآن عن مؤدٍ."),
        ("OfferCreated",false) => new("New offer", "You have a new booking offer. Please respond before it expires."),
        ("OfferCreated",true) => new("عرض جديد", "لديك عرض حجز جديد. يرجى الرد قبل انتهاء العرض."),
        ("BookingCompleted",false) => new("Booking completed", "Your booking has been completed and its proof approved."),
        ("BookingCompleted",true) => new("اكتمل الحجز", "اكتمل حجزك وتم اعتماد الإثبات."),
        ("RefundRequired",false) => new("Refund requested", "Your booking cannot proceed. A full refund has been requested."),
        ("RefundRequired",true) => new("تم طلب الاسترداد", "لا يمكن تنفيذ حجزك. تم طلب استرداد كامل المبلغ."),
        (_,false) => new("Payment review required", "A payment needs accountant review. Check the operations report."),
        (_,true) => new("مراجعة دفعة مطلوبة", "تحتاج دفعة إلى مراجعة المحاسب. يرجى مراجعة تقرير العمليات.")
    };
}
public sealed class InvalidDeviceTokenException : Exception;
public interface INotificationTransport
{
    Task EmailAsync(string address, NotificationText text, Guid messageId, CancellationToken ct);
    Task PushAsync(string token, NotificationText text, CancellationToken ct);
}
public sealed class NotificationTransport(HttpClient http, IConfiguration config) : INotificationTransport, IDisposable
{
    private readonly Lazy<FirebaseApp> _firebase = new(() => FirebaseApp.Create(new AppOptions
    {
        ProjectId = config["Firebase:ProjectId"] ?? throw new PaymentDependencyException(),
        Credential = CredentialFactory.FromJson<ServiceAccountCredential>(config["Firebase:CredentialsJson"] ?? throw new PaymentDependencyException()).ToGoogleCredential()
    }, Guid.NewGuid().ToString("N")));
    public async Task EmailAsync(string address, NotificationText text, Guid messageId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Notifications:EmailApiKey"] ?? throw new PaymentDependencyException());
        request.Headers.Add("Idempotency-Key", messageId.ToString("N"));
        request.Content = JsonContent.Create(new { from = config["Notifications:EmailFrom"] ?? throw new PaymentDependencyException(), to = new[] { address }, subject = text.Subject, text = text.Body });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new PaymentDependencyException();
    }
    public async Task PushAsync(string token, NotificationText text, CancellationToken ct)
    {
        // This endpoint accepts registration tokens, not FIDs. Keep the supported legacy field until clients migrate;
        // assigning an existing registration token to Fid would change its meaning.
#pragma warning disable CS0618
        try { _ = await FirebaseMessaging.GetMessaging(_firebase.Value).SendAsync(new Message { Token = token, Notification = new Notification { Title = text.Subject, Body = text.Body } }, ct); }
#pragma warning restore CS0618
        catch (FirebaseMessagingException error) when (error.MessagingErrorCode == MessagingErrorCode.Unregistered) { throw new InvalidDeviceTokenException(); }
    }
    public void Dispose() { if (_firebase.IsValueCreated) _firebase.Value.Delete(); }
}
public static class Events
{
    public static void Add(AppDbContext db, Guid bookingId, string type, string key, object payload, DateTimeOffset now)
    {
        var id = Guid.NewGuid(); db.OutboxMessages.Add(new OutboxMessage { Id = id, BookingId = bookingId, Type = type, DedupKey = key, Payload = JsonSerializer.Serialize(payload) });
        WorkQueue.Enqueue(db, WorkKind.Outbox, id, now);
    }
}
public sealed class BookingEventPublisher(AppDbContext db, WorkQueue queue, INotificationTransport transport, IConfiguration config) : IPaymentEventPublisher
{
    public async Task PublishAsync(PaymentEvent message, CancellationToken ct)
    {
        if (message.Type == "Notification")
        {
            var delivery = JsonSerializer.Deserialize<Delivery>(message.Payload) ?? throw new PaymentDependencyException();
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == delivery.UserId, ct);
            if (user.Status != UserStatus.Active) return;
            var text = NotificationTemplates.Render(delivery.EventType, user.Locale);
            if (delivery.Channel == "Email")
            {
                var email = config[$"Notifications:EmailRecipients:{user.Id:D}"];
                if (string.IsNullOrWhiteSpace(email)) throw new PaymentDependencyException();
                await transport.EmailAsync(email, text, message.Id, ct);
            }
            else
            {
                var device = await db.DeviceTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.DeviceId && x.UserId == user.Id && x.InvalidatedAt == null, ct);
                if (device is null) return;
                try { await transport.PushAsync(device.Token, text, ct); }
                catch (InvalidDeviceTokenException) { await db.DeviceTokens.Where(x => x.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.InvalidatedAt, DateTimeOffset.UtcNow), ct); }
            }
            return;
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM app.\"Bookings\" WHERE \"Id\"={message.BookingId} FOR UPDATE").SingleAsync(ct);
        var now = await queue.Now(ct);
        if (message.Type == "BookingPaid" && !await db.JobRuns.AnyAsync(x => x.JobName == $"Assign:{booking.Id:N}", ct)) WorkQueue.Enqueue(db, WorkKind.Assign, booking.Id, now);
        var recipients = new List<Guid>();
        if (message.Type is "BookingPaid" or "BookingCompleted" or "RefundRequired")
        {
            // A delayed paid event must not promise assignment after cancellation/refund already won.
            if(message.Type!="BookingPaid" || booking.State is not (BookingState.Cancelled or BookingState.Refunded)) recipients.Add(booking.CustomerId);
        }
        else if (message.Type == "OfferCreated")
        {
            using var payload=JsonDocument.Parse(message.Payload);
            var offerId=payload.RootElement.GetProperty("OfferId").GetGuid();
            var performer = await db.PerformerOffers.Where(x => x.Id==offerId && x.BookingId == booking.Id && x.Status == "Pending" && x.ExpiresAt>now).Select(x => (Guid?)x.PerformerId).SingleOrDefaultAsync(ct);
            if (performer is not null) recipients.Add(performer.Value);
        }
        else if (message.Type is "PaymentMismatch" or "AccountantAlert") recipients.AddRange(await db.Users.Where(x => x.Status == UserStatus.Active && (x.Role == ActorRole.Accountant || x.Role == ActorRole.Admin)).Select(x => x.Id).ToListAsync(ct));
        else throw new PaymentDependencyException(); // Unknown events must dead-letter visibly, never silently disappear.
        foreach (var recipient in recipients.Distinct())
        {
            // Email addresses are trusted operator configuration, never identity keys or user-controlled role links.
            if (!string.IsNullOrWhiteSpace(config[$"Notifications:EmailRecipients:{recipient:D}"]))
                await AddDelivery(message, new(recipient,"Email",message.Type,null), now, ct);
            foreach (var device in await db.DeviceTokens.Where(x => x.UserId == recipient && x.InvalidatedAt == null).ToListAsync(ct))
                await AddDelivery(message, new(recipient,"Push",message.Type,device.Id), now, ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task AddDelivery(PaymentEvent source, Delivery delivery, DateTimeOffset now, CancellationToken ct)
    {
        var key = $"notification:{source.Id:N}:{delivery.UserId:N}:{delivery.Channel}:{delivery.DeviceId?.ToString("N")}";
        if (!await db.OutboxMessages.AnyAsync(x => x.DedupKey == key, ct)) Events.Add(db,source.BookingId,"Notification",key,delivery,now);
    }
}
