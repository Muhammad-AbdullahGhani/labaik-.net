using Labaik.Domain.Bookings;
using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Identity;
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
    public DbSet<RefreshFamily> RefreshFamilies => Set<RefreshFamily>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<CapturedPayment> CapturedPayments => Set<CapturedPayment>();
    public DbSet<WebhookInbox> WebhookInbox => Set<WebhookInbox>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<RefundRequest> RefundRequests => Set<RefundRequest>();
    public DbSet<PerformerOffer> PerformerOffers => Set<PerformerOffer>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<Earning> Earnings => Set<Earning>();
    public DbSet<PayoutBatch> PayoutBatches => Set<PayoutBatch>();
    public DbSet<PayoutItem> PayoutItems => Set<PayoutItem>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("app");
        CoreModel.Configure(modelBuilder);
        AuthModel.Configure(modelBuilder);
        PaymentModel.Configure(modelBuilder);
        OperationsModel.Configure(modelBuilder);
        PayoutModel.Configure(modelBuilder);
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
