using Labaik.Domain.Bookings;
using Labaik.Domain.Pricing;
using Labaik.Domain.Users;
using Labaik.Domain.Vouchers;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class CoreDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithPassword(Guid.NewGuid().ToString("N")).Build();
    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), options => options.MigrationsHistoryTable("__EFMigrationsHistory", "app")).Options);
    public string ConnectionString => _postgres.GetConnectionString();
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

public sealed class CoreSchemaTests(CoreDatabase database) : IClassFixture<CoreDatabase>
{
    private static (User User, Booking Booking) Seed()
    {
        var user = new User(Guid.NewGuid(), "test-project", Guid.NewGuid().ToString("N"), ActorRole.Customer, DateTimeOffset.UtcNow);
        var booking = new Booking(Guid.NewGuid(), user.Id, new DateOnly(2026, 12, 1),
            new PriceQuote(new Money(100), default, default), DateTimeOffset.UtcNow);
        return (user, booking);
    }
    [Fact]
    [Trait("Category", "Docker")]
    public async Task Migration_applies_and_named_checks_reject_invalid_rows()
    {
        await using var db = database.CreateContext();
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(db.Database.HasPendingModelChanges());
        var (user, booking) = Seed();
        var voucher = new Voucher(Guid.NewGuid(), "V" + Guid.NewGuid().ToString("N"), VoucherKind.Percentage, null, 1000, 5, 2, DateTimeOffset.UtcNow.AddDays(1));
        db.AddRange(user, booking, voucher, new DailyCapacity(new DateOnly(2026, 12, 1)));
        await db.SaveChangesAsync();
        async Task Check(string sql, string constraint, Guid? id = null)
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, id is null ? [] : new object[] { id.Value }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal(constraint, failure.ConstraintName);
        }
        // Deliberately bypass constructors to prove the database itself rejects corrupt data.
        await Check("UPDATE app.\"DailyCapacities\" SET \"Allocated\"=51", "CK_DailyCapacity_Bounds");
        await Check("UPDATE app.\"Bookings\" SET \"BaseMinor\"=-1, \"TotalMinor\"=-1 WHERE \"Id\"={0}", "CK_Bookings_Money", booking.Id);
        await Check("UPDATE app.\"Bookings\" SET \"TotalMinor\"=99 WHERE \"Id\"={0}", "CK_Bookings_Total", booking.Id);
        await Check("UPDATE app.\"Bookings\" SET \"Currency\"='USD' WHERE \"Id\"={0}", "CK_Bookings_Currency", booking.Id);
        await Check("UPDATE app.\"Vouchers\" SET \"BasisPoints\"=NULL", "CK_Vouchers_Discount");
        await Check("UPDATE app.\"Users\" SET \"Role\"='System'", "CK_Users_Role");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Docker")]
    public async Task Stale_booking_update_fails_without_mixed_state(bool synchronousWinner)
    {
        var (user, booking) = Seed();
        await using (var seed = database.CreateContext()) { seed.AddRange(user, booking); await seed.SaveChangesAsync(); }
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var a = await first.Bookings.SingleAsync(x => x.Id == booking.Id);
        var b = await second.Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.True(a.TryTransition(BookingState.PendingPayment, ActorRole.Customer).IsSuccess);
        Assert.True(b.TryTransition(BookingState.Cancelled, ActorRole.Customer).IsSuccess);
        if (synchronousWinner)
        {
            first.SaveChanges();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
        else
        {
            await first.SaveChangesAsync();
            Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
        }
        await using var check = database.CreateContext();
        var saved = await check.Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.Equal(BookingState.PendingPayment, saved.State);
        Assert.Equal(2, saved.Version);
        Assert.Equal(100, saved.TotalMinor);
    }
}
