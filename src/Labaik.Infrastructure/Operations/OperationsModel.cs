using Labaik.Domain.Bookings;
using Labaik.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Operations;

internal static class OperationsModel
{
    public static void Configure(ModelBuilder model)
    {
        var offers = model.Entity<PerformerOffer>();
        offers.ToTable("PerformerOffers", t => t.HasCheckConstraint("CK_Offers_Status", "\"Status\" IN ('Pending','Accepted','Expired','Cancelled','Finished')"));
        offers.HasKey(x => x.Id).HasName("PK_PerformerOffers");
        offers.HasIndex(x => x.BookingId).IsUnique().HasFilter("\"Status\" IN ('Pending','Accepted')").HasDatabaseName("UX_Offers_ActiveBooking");
        offers.HasIndex(x => x.PerformerId).IsUnique().HasFilter("\"Status\" IN ('Pending','Accepted')").HasDatabaseName("UX_Offers_ActivePerformer");
        offers.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("IX_Offers_Expiry");
        offers.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Offers_Bookings");
        offers.HasOne<User>().WithMany().HasForeignKey(x => x.PerformerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Offers_Users");
        var files = model.Entity<StoredFile>();
        files.ToTable("StoredFiles", t =>
        {
            t.HasCheckConstraint("CK_Files_Status", "\"Status\" IN ('PendingUpload','Uploaded','Approved')");
            t.HasCheckConstraint("CK_Files_Kind", "\"Kind\" IN ('Proof','Passport')");
            t.HasCheckConstraint("CK_Files_Size", "\"Size\" >= 0 AND \"Size\" <= 52428800");
        });
        files.HasKey(x => x.Id).HasName("PK_StoredFiles");
        files.HasIndex(x => x.ObjectKey).IsUnique().HasDatabaseName("UX_Files_ObjectKey");
        files.HasIndex(x => new { x.BookingId, x.Kind }).HasDatabaseName("IX_Files_BookingKind");
        files.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Files_Owner");
        files.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Files_Booking");
    }
}
