using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Operations;

internal static class PayoutModel
{
    public static void Configure(ModelBuilder model)
    {
        var earnings = model.Entity<Earning>();
        earnings.ToTable("Earnings", t =>
        {
            t.HasCheckConstraint("CK_Earnings_Money", "\"AmountMinor\">0 AND \"Currency\"='SAR'");
            t.HasCheckConstraint("CK_Earnings_Status", "\"Status\" IN ('Available','Reserved','Paid')");
        });
        earnings.HasKey(x => x.Id).HasName("PK_Earnings");
        earnings.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("UX_Earnings_Booking");
        earnings.HasIndex(x => new { x.PerformerId, x.Status, x.CompletedAt }).HasDatabaseName("IX_Earnings_Pool");
        earnings.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Earnings_Booking");
        earnings.HasOne<User>().WithMany().HasForeignKey(x => x.PerformerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Earnings_Performer");
        var batches = model.Entity<PayoutBatch>();
        batches.ToTable("PayoutBatches", t =>
        {
            t.HasCheckConstraint("CK_Batches_Money", "\"TotalMinor\">0 AND \"Currency\"='SAR'");
            t.HasCheckConstraint("CK_Batches_Status", "\"Status\" IN ('Pending','Paid','Failed')");
            t.HasCheckConstraint("CK_Batches_Reference", "\"Status\"='Pending' OR \"BankReference\" IS NOT NULL");
        });
        batches.HasKey(x => x.Id).HasName("PK_PayoutBatches");
        batches.HasIndex(x => x.RequestKey).IsUnique().HasDatabaseName("UX_Batches_RequestKey");
        batches.HasIndex(x => x.BankReference).IsUnique().HasFilter("\"BankReference\" IS NOT NULL").HasDatabaseName("UX_Batches_BankReference");
        batches.HasIndex(x => new { x.WeekStart, x.PerformerId }).HasDatabaseName("IX_Batches_Week");
        batches.HasOne<User>().WithMany().HasForeignKey(x => x.PerformerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Batches_Performer");
        var items = model.Entity<PayoutItem>();
        items.ToTable("PayoutItems", t =>
        {
            t.HasCheckConstraint("CK_Items_Money", "\"AmountMinor\">0 AND \"Currency\"='SAR'");
            t.HasCheckConstraint("CK_Items_Status", "\"Status\" IN ('Pending','Paid','Failed')");
        });
        items.HasKey(x => x.Id).HasName("PK_PayoutItems");
        items.HasIndex(x => new { x.BatchId, x.EarningId }).IsUnique().HasDatabaseName("UX_Items_BatchEarning");
        items.HasIndex(x => x.EarningId).IsUnique().HasFilter("\"Status\" IN ('Pending','Paid')").HasDatabaseName("UX_Items_ClaimedEarning");
        items.HasOne<PayoutBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Items_Batch");
        items.HasOne<Earning>().WithMany().HasForeignKey(x => x.EarningId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Items_Earning");
    }
}
