using Labaik.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Identity;

internal static class AuthModel
{
    public static void Configure(ModelBuilder model)
    {
        var family = model.Entity<RefreshFamily>();
        family.ToTable("RefreshFamilies");
        family.HasKey(x => x.Id).HasName("PK_RefreshFamilies");
        family.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_RefreshFamilies_Users");
        family.HasIndex(x => x.UserId).HasDatabaseName("IX_RefreshFamilies_User");
        var token = model.Entity<RefreshToken>();
        token.ToTable("RefreshTokens", t =>
        {
            t.HasCheckConstraint("CK_RefreshTokens_Hash", "length(\"Hash\")=64");
            t.HasCheckConstraint("CK_RefreshTokens_Rotation", "(\"ConsumedAt\" IS NULL AND \"RetryUntil\" IS NULL AND \"RetryKeyHash\" IS NULL AND \"EncryptedSuccessor\" IS NULL) OR (\"ConsumedAt\" IS NOT NULL AND \"RetryUntil\" IS NOT NULL AND \"RetryKeyHash\" IS NOT NULL)");
        });
        token.HasKey(x => x.Id).HasName("PK_RefreshTokens");
        token.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        token.Property(x => x.RetryKeyHash).HasMaxLength(64);
        token.HasIndex(x => x.Hash).IsUnique().HasDatabaseName("UX_RefreshTokens_Hash");
        token.HasIndex(x => x.ParentId).IsUnique().HasDatabaseName("UX_RefreshTokens_Parent");
        token.HasIndex(x => x.RetryUntil).HasDatabaseName("IX_RefreshTokens_RecoveryExpiry");
        token.HasOne<RefreshFamily>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_RefreshTokens_Family");
        token.HasAlternateKey(x => new { x.Id, x.FamilyId }).HasName("AK_RefreshTokens_IdFamily");
        token.HasOne<RefreshToken>().WithMany().HasForeignKey(x => new { x.ParentId, x.FamilyId })
            .HasPrincipalKey(x => new { x.Id, x.FamilyId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_RefreshTokens_ParentFamily");
        var buckets = model.Entity<AuthRateBucket>();
        buckets.ToTable("AuthRateBuckets", t => t.HasCheckConstraint("CK_AuthRateBuckets_Count", "\"Count\" > 0"));
        buckets.HasKey(x => x.Key).HasName("PK_AuthRateBuckets");
        buckets.Property(x => x.Key).HasMaxLength(64);
        buckets.HasIndex(x => x.WindowStart).HasDatabaseName("IX_AuthRateBuckets_Expiry");
    }
}
