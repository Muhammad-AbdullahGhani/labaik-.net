using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Pricing;
using Labaik.Domain.Vouchers;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Payments;

public sealed class BookingService(AppDbContext db, BookingPolicy policy)
{
    public async Task<Booking> CreateAsync(Guid actorId, DateOnly date, string? voucherCode, CancellationToken ct = default)
    {
        var actor = await db.Users.SingleOrDefaultAsync(x => x.Id == actorId, ct);
        if (actor is null || actor.Status != Domain.Users.UserStatus.Active || actor.Role is not (ActorRole.Customer or ActorRole.Admin)) throw new PaymentConflictException("Customer account required.");
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        if (date < DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh")).DateTime)) throw new PaymentConflictException("Service date is in the past.");
        Voucher? voucher = null;
        if (voucherCode is not null) voucher = await db.Vouchers.SingleOrDefaultAsync(x => x.CodeNormalized == voucherCode.Trim().ToUpperInvariant(), ct) ?? throw new PaymentConflictException("Unknown voucher.");
        var pricing = PriceCalculator.Calculate(new(new Money(policy.BaseMinor), policy.DateAddons.ToDictionary(x => x.Key, x => new Money(x.Value))), date, voucher, default, now);
        if (!pricing.IsSuccess) throw new PaymentConflictException(pricing.Reason!);
        var booking = new Booking(Guid.NewGuid(), actorId, date, pricing.Quote!, now); db.Bookings.Add(booking); await db.SaveChangesAsync(ct); return booking;
    }
}
