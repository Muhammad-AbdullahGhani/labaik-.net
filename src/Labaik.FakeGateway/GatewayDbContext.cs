using Microsoft.EntityFrameworkCore;

namespace Labaik.FakeGateway;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<GatewayCheckout> Checkouts => Set<GatewayCheckout>();
    public DbSet<GatewayCharge> Charges => Set<GatewayCharge>();
    public DbSet<GatewayRefund> Refunds => Set<GatewayRefund>();
    public DbSet<GatewayEvent> Events => Set<GatewayEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("fake_gateway");
        var checkouts = model.Entity<GatewayCheckout>();
        checkouts.ToTable("Checkouts", t =>
        {
            t.HasCheckConstraint("CK_GatewayCheckouts_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
            t.HasCheckConstraint("CK_GatewayCheckouts_Status", "\"Status\" IN ('Pending','Succeeded','Failed')");
            t.HasCheckConstraint("CK_GatewayCheckouts_Capture", "(\"Status\"='Succeeded') = (\"TransactionId\" IS NOT NULL)");
        });
        checkouts.HasKey(x => x.Id).HasName("PK_GatewayCheckouts");
        checkouts.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        checkouts.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        checkouts.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_GatewayCheckouts_Key");
        var charges = model.Entity<GatewayCharge>();
        charges.ToTable("Charges", t => t.HasCheckConstraint("CK_GatewayCharges_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'"));
        charges.HasKey(x => x.Id).HasName("PK_GatewayCharges");
        charges.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("UX_GatewayCharges_Booking");
        charges.HasIndex(x => x.CheckoutId).IsUnique().HasDatabaseName("UX_GatewayCharges_Checkout");
        charges.HasOne<GatewayCheckout>().WithMany().HasForeignKey(x => x.CheckoutId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_GatewayCharges_Checkout");
        var refunds = model.Entity<GatewayRefund>();
        refunds.ToTable("Refunds", t => t.HasCheckConstraint("CK_GatewayRefunds_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'"));
        refunds.HasKey(x => x.Id).HasName("PK_GatewayRefunds");
        refunds.HasIndex(x => x.Key).IsUnique().HasDatabaseName("UX_GatewayRefunds_Key");
        refunds.HasIndex(x => x.TransactionId).IsUnique().HasDatabaseName("UX_GatewayRefunds_Charge");
        refunds.HasOne<GatewayCharge>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_GatewayRefunds_Charge");
        var events = model.Entity<GatewayEvent>();
        events.ToTable("Events", t =>
        {
            t.HasCheckConstraint("CK_GatewayEvents_Status", "\"Status\" IN ('Pending','Processing','Completed','DeadLetter')");
            t.HasCheckConstraint("CK_GatewayEvents_Counters", "\"Attempts\" >= 0 AND \"ClaimVersion\" >= 0");
        });
        events.HasKey(x => x.Id).HasName("PK_GatewayEvents");
        events.HasIndex(x => new { x.Status, x.AvailableAt }).HasDatabaseName("IX_GatewayEvents_Due");
        events.HasOne<GatewayCheckout>().WithMany().HasForeignKey(x => x.CheckoutId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_GatewayEvents_Checkout");
    }
}
