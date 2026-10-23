using Labaik.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Payments;

internal static class PaymentModel
{
    public static void Configure(ModelBuilder model)
    {
        var attempts = model.Entity<PaymentAttempt>();
        attempts.ToTable("PaymentAttempts", t =>
        {
            t.HasCheckConstraint("CK_PaymentAttempts_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'");
            t.HasCheckConstraint("CK_PaymentAttempts_Status", "\"Status\" IN ('Creating','Pending','Unknown','Succeeded','Failed')");
            t.HasCheckConstraint("CK_PaymentAttempts_Sequence", "\"Sequence\" > 0");
        });
        attempts.HasKey(x => x.Id).HasName("PK_PaymentAttempts");
        attempts.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        attempts.Property(x => x.RequestKey).HasMaxLength(128).IsRequired();
        attempts.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        attempts.Property(x => x.GatewayKey).HasMaxLength(128).IsRequired();
        attempts.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        attempts.HasIndex(x => new { x.BookingId, x.Sequence }).IsUnique().HasDatabaseName("UX_Attempts_Sequence");
        attempts.HasIndex(x => new { x.BookingId, x.RequestKey }).IsUnique().HasDatabaseName("UX_Attempts_RequestKey");
        attempts.HasIndex(x => x.GatewayKey).IsUnique().HasDatabaseName("UX_Attempts_GatewayKey");
        attempts.HasIndex(x => x.CheckoutId).IsUnique().HasDatabaseName("UX_Attempts_Checkout");
        attempts.HasIndex(x => x.BookingId).IsUnique().HasFilter("\"Status\" IN ('Creating','Pending','Unknown','Succeeded')").HasDatabaseName("UX_Attempts_LiveBooking");
        attempts.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Attempts_Bookings");
        var captures = model.Entity<CapturedPayment>();
        captures.ToTable("CapturedPayments", t => t.HasCheckConstraint("CK_CapturedPayments_Money", "\"AmountMinor\" > 0 AND \"Currency\"='SAR'"));
        captures.HasKey(x => x.Id).HasName("PK_CapturedPayments");
        captures.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        captures.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("UX_Captures_Booking");
        captures.HasIndex(x => x.AttemptId).IsUnique().HasDatabaseName("UX_Captures_Attempt");
        captures.HasIndex(x => x.GatewayTransactionId).IsUnique().HasDatabaseName("UX_Captures_Transaction");
        captures.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Captures_Bookings");
        captures.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Captures_Attempts");
        var inbox = model.Entity<WebhookInbox>();
        inbox.ToTable("WebhookInbox", t => t.HasCheckConstraint("CK_Inbox_Status", "\"Status\" IN ('Pending','Processed','Rejected')"));
        inbox.HasKey(x => x.Id).HasName("PK_WebhookInbox");
        inbox.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        inbox.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        inbox.Property(x => x.Signature).HasMaxLength(64).IsRequired();
        inbox.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        inbox.HasIndex(x => new { x.Provider, x.EventId }).IsUnique().HasDatabaseName("UX_Inbox_ProviderEvent");
        var outbox = model.Entity<OutboxMessage>();
        outbox.ToTable("OutboxMessages"); outbox.HasKey(x => x.Id).HasName("PK_OutboxMessages");
        outbox.Property(x => x.DedupKey).HasMaxLength(160).IsRequired();
        outbox.Property(x => x.Type).HasMaxLength(64).IsRequired();
        outbox.HasIndex(x => x.DedupKey).IsUnique().HasDatabaseName("UX_Outbox_DedupKey");
        outbox.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Outbox_Bookings");
        var jobs = model.Entity<JobRun>();
        jobs.ToTable("JobRuns", t =>
        {
            t.HasCheckConstraint("CK_JobRuns_State", "\"Status\" IN ('Pending','Processing','Completed','DeadLetter')");
            t.HasCheckConstraint("CK_JobRuns_Counters", "\"Attempts\" >= 0 AND \"ClaimVersion\" >= 0");
            t.HasCheckConstraint("CK_JobRuns_Kind", "\"Kind\" IN ('RecoverCheckout','Inbox','Outbox')");
            t.HasCheckConstraint("CK_JobRuns_Lease", "\"Status\" <> 'Processing' OR (\"LeaseOwner\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL)");
        });
        jobs.HasKey(x => x.Id).HasName("PK_JobRuns");
        jobs.Property(x => x.JobName).HasMaxLength(128).IsRequired();
        jobs.Property(x => x.LastError).HasMaxLength(64);
        jobs.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        jobs.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        jobs.HasIndex(x => new { x.JobName, x.ScheduledFor }).IsUnique().HasDatabaseName("UX_JobRuns_Schedule");
        jobs.HasIndex(x => new { x.Status, x.AvailableAt, x.LeaseUntil }).HasDatabaseName("IX_JobRuns_Due");
    }
}
