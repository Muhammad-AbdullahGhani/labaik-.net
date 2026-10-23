using Labaik.Application.Identity;
using Labaik.Domain.Bookings;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Identity;

// Invoked only by an explicit operator CLI, never registered as a public HTTP operation.
public sealed class VerifiedUidProvisioner(AppDbContext db, IFirebaseTokenVerifier verifier, AuthSettings settings)
{
    public async Task ProvisionAsync(string token, ActorRole role, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(role) || role is ActorRole.Customer or ActorRole.System) throw new ArgumentException("A staff/performer role is required.");
        var identity = await verifier.VerifyAsync(token, ct);
        if (identity.Project != settings.FirebaseProject) throw new InvalidFirebaseTokenException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var id = Guid.NewGuid(); var roleName = role.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."Users" ("Id","FirebaseProject","FirebaseUid","Role","Status","Locale","LastOfferedSequence","CreatedAt")
            VALUES ({id},{identity.Project},{identity.Uid},{roleName},'Active','en',0,clock_timestamp())
            ON CONFLICT ("FirebaseProject","FirebaseUid") DO UPDATE SET "Role"=EXCLUDED."Role"
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE app."RefreshFamilies" SET "RevokedAt"=clock_timestamp()
            WHERE "UserId" IN (SELECT "Id" FROM app."Users" WHERE "FirebaseProject"={identity.Project} AND "FirebaseUid"={identity.Uid})
            """, ct);
        await tx.CommitAsync(ct);
    }
}
