using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Labaik.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Persistence;

internal static class CoreModel
{
    public static void Configure(ModelBuilder model)
    {
        var users = model.Entity<User>();
        users.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('Customer','Performer','Supervisor','Accountant','Admin')");
            t.HasCheckConstraint("CK_Users_Status", "\"Status\" IN ('Active','Disabled')");
            t.HasCheckConstraint("CK_Users_Locale", "\"Locale\" IN ('en','ar')");
            t.HasCheckConstraint("CK_Users_Identity", "length(btrim(\"FirebaseProject\")) > 0 AND length(btrim(\"FirebaseUid\")) > 0");
            t.HasCheckConstraint("CK_Users_Sequence", "\"LastOfferedSequence\" >= 0");
        });
        users.HasKey(x => x.Id).HasName("PK_Users");
        users.Property(x => x.FirebaseProject).HasMaxLength(128).IsRequired();
        users.Property(x => x.FirebaseUid).HasMaxLength(128).IsRequired();
        users.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        users.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        users.Property(x => x.Locale).HasMaxLength(2).IsRequired();
        users.HasIndex(x => new { x.FirebaseProject, x.FirebaseUid }).IsUnique().HasDatabaseName("UX_Users_FirebaseIdentity");
        users.HasIndex(x => new { x.Role, x.Status, x.LastOfferedSequence }).HasDatabaseName("IX_Users_Assignment");

        var bookings = model.Entity<Booking>();
        bookings.ToTable("Bookings", t =>
        {
            t.HasCheckConstraint("CK_Bookings_Money", "\"BaseMinor\" >= 0 AND \"AddonMinor\" >= 0 AND \"DiscountMinor\" >= 0 AND \"TotalMinor\" >= 0");
            // numeric casts prevent overflowing the CHECK expression itself at bigint boundaries.
            t.HasCheckConstraint("CK_Bookings_Total", "\"TotalMinor\"::numeric = \"BaseMinor\"::numeric + \"AddonMinor\"::numeric - \"DiscountMinor\"::numeric AND \"BaseMinor\"::numeric + \"AddonMinor\"::numeric <= 9223372036854775807");
            t.HasCheckConstraint("CK_Bookings_Currency", "\"Currency\" = 'SAR'");
            t.HasCheckConstraint("CK_Bookings_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_Bookings_State", "\"State\" IN ('Draft','PendingPayment','Paid','Offered','Accepted','InProgress','ProofSubmitted','Completed','Cancelled','Refunded','Disputed')");
            t.HasCheckConstraint("CK_Bookings_Checkout", "\"State\" <> 'PendingPayment' OR \"TotalMinor\" > 0");
        });
        bookings.HasKey(x => x.Id).HasName("PK_Bookings");
        bookings.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        bookings.Property(x => x.Version).IsConcurrencyToken();
        bookings.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        bookings.Property(x => x.CancellationReason).HasMaxLength(256);
        bookings.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Bookings_Users");
        bookings.HasOne<Voucher>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Bookings_Vouchers");
        bookings.HasIndex(x => new { x.CustomerId, x.CreatedAt }).HasDatabaseName("IX_Bookings_Customer");
        bookings.HasIndex(x => new { x.State, x.ServiceDate }).HasDatabaseName("IX_Bookings_StateDate");

        var capacity = model.Entity<DailyCapacity>();
        capacity.ToTable("DailyCapacities", t => t.HasCheckConstraint("CK_DailyCapacity_Bounds", "\"Limit\" >= 0 AND \"Allocated\" >= 0 AND \"Allocated\" <= \"Limit\""));
        capacity.HasKey(x => x.ServiceDate).HasName("PK_DailyCapacities");

        var vouchers = model.Entity<Voucher>();
        vouchers.ToTable("Vouchers", t =>
        {
            t.HasCheckConstraint("CK_Vouchers_Code", "\"CodeNormalized\" ~ '^[A-Z0-9_-]{1,64}$'");
            t.HasCheckConstraint("CK_Vouchers_Limits", "\"MaxUses\" > 0 AND \"PerUserLimit\" > 0");
            t.HasCheckConstraint("CK_Vouchers_Currency", "\"Currency\" = 'SAR'");
            // IS NOT NULL matters: a SQL CHECK treats UNKNOWN as success.
            t.HasCheckConstraint("CK_Vouchers_Discount", "(\"Kind\" = 'Percentage' AND \"FixedMinor\" IS NULL AND \"BasisPoints\" IS NOT NULL AND \"BasisPoints\" BETWEEN 0 AND 10000) OR (\"Kind\" = 'Fixed' AND \"BasisPoints\" IS NULL AND \"FixedMinor\" IS NOT NULL AND \"FixedMinor\" >= 0)");
        });
        vouchers.HasKey(x => x.Id).HasName("PK_Vouchers");
        vouchers.Property(x => x.CodeNormalized).HasMaxLength(64).IsRequired();
        vouchers.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        vouchers.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        vouchers.HasIndex(x => x.CodeNormalized).IsUnique().HasDatabaseName("UX_Vouchers_Code");

        var redemptions = model.Entity<VoucherRedemption>();
        redemptions.ToTable("VoucherRedemptions", t => t.HasCheckConstraint("CK_VoucherRedemptions_Status", "\"Status\" IN ('Reserved','Consumed','Released')"));
        redemptions.HasKey(x => x.Id).HasName("PK_VoucherRedemptions");
        redemptions.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        redemptions.HasOne<Voucher>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Redemptions_Vouchers");
        redemptions.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Redemptions_Users");
        redemptions.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Redemptions_Bookings");
        redemptions.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("UX_Redemptions_Booking");
        redemptions.HasIndex(x => new { x.VoucherId, x.UserId, x.Status }).HasDatabaseName("IX_Redemptions_Usage");
    }
}
