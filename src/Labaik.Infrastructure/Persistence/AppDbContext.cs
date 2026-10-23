using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<DailyCapacity> DailyCapacities => Set<DailyCapacity>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherRedemption> VoucherRedemptions => Set<VoucherRedemption>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("app");
        CoreModel.Configure(modelBuilder);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AdvanceBookingVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AdvanceBookingVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void AdvanceBookingVersions()
    {
        // The original version remains in EF's WHERE predicate; every tracked update advances it.
        // Future ExecuteUpdate/raw SQL writers must explicitly check and increment Version too.
        foreach (var entry in ChangeTracker.Entries<Booking>().Where(e => e.State == EntityState.Modified))
            entry.Property(b => b.Version).CurrentValue = checked(entry.Property(b => b.Version).OriginalValue + 1);
    }
}
