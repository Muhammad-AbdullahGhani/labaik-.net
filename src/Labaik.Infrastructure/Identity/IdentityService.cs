using System.Security.Cryptography;
using System.Text;
using Labaik.Application.Identity;
using Labaik.Domain.Users;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Labaik.Infrastructure.Identity;

public sealed class IdentityService(AppDbContext db, IFirebaseTokenVerifier verifier, AuthSettings settings,
    TokenCodec codec, AuthRateLimiter limiter)
{
    private static bool IsToken(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public static bool IsRequestKey(string? value) => value is not null && Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    public async Task<AuthResult> LoginAsync(string idToken, string ip, CancellationToken ct = default)
    {
        if (!await limiter.IpAsync(ip, ct)) return AuthResult.Reject(AuthError.RateLimited);
        VerifiedFirebaseIdentity identity;
        try { identity = await verifier.VerifyAsync(idToken, ct); }
        catch (InvalidFirebaseTokenException) { return AuthResult.Reject(AuthError.InvalidCredentials); }
        if (identity.Project != settings.FirebaseProject || string.IsNullOrWhiteSpace(identity.Uid) || identity.Uid.Length > 128)
            return AuthResult.Reject(AuthError.InvalidCredentials);
        if (!await limiter.AccountAsync(identity.Project, identity.Uid, ct)) return AuthResult.Reject(AuthError.RateLimited);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = await Now(ct); var userId = Guid.NewGuid();
        // A unique verified project/UID is the only conflict target. Email and incoming roles are ignored.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app."Users" ("Id","FirebaseProject","FirebaseUid","Role","Status","Locale","LastOfferedSequence","CreatedAt")
            VALUES ({userId},{identity.Project},{identity.Uid},'Customer','Active','en',0,{now})
            ON CONFLICT ("FirebaseProject","FirebaseUid") DO NOTHING
            """, ct);
        var user = await db.Users.SingleAsync(x => x.FirebaseProject == identity.Project && x.FirebaseUid == identity.Uid, ct);
        if (user.Status != UserStatus.Active) return AuthResult.Reject(AuthError.InvalidCredentials);
        var family = new RefreshFamily { Id = Guid.NewGuid(), UserId = user.Id, ExpiresAt = now.AddDays(30) };
        var raw = TokenCodec.RandomToken();
        db.RefreshFamilies.Add(family);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), FamilyId = family.Id, Hash = TokenCodec.Hash(raw), ExpiresAt = family.ExpiresAt });
        var tokens = codec.Issue(user, raw, now);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(tokens, null);
    }

    public async Task<AuthResult> RefreshAsync(string raw, string requestKey, string ip, CancellationToken ct = default)
    {
        if (!await limiter.IpAsync(ip, ct)) return AuthResult.Reject(AuthError.RateLimited);
        if (!IsToken(raw) || !IsRequestKey(requestKey)) return AuthResult.Reject(AuthError.InvalidCredentials);
        var hash = TokenCodec.Hash(raw);
        var tokenInfo = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Hash == hash, ct);
        if (tokenInfo is null) return AuthResult.Reject(AuthError.InvalidCredentials);
        var user = await (from family in db.RefreshFamilies from account in db.Users
            where family.Id == tokenInfo.FamilyId && account.Id == family.UserId select account).SingleAsync(ct);
        // Possession of a stored refresh secret identifies the account; never throttle a caller-supplied user/email.
        if (!await limiter.AccountAsync(user.FirebaseProject, user.FirebaseUid, ct)) return AuthResult.Reject(AuthError.RateLimited);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockedFamily = await db.RefreshFamilies.FromSqlInterpolated($"""
            SELECT * FROM app."RefreshFamilies" WHERE "Id"={tokenInfo.FamilyId} FOR UPDATE
            """).SingleAsync(ct);
        var now = await Now(ct);
        if (lockedFamily.RevokedAt is not null || lockedFamily.ExpiresAt <= now)
            return AuthResult.Reject(AuthError.InvalidCredentials);
        var parent = await db.RefreshTokens.SingleAsync(x => x.Id == tokenInfo.Id, ct);
        if (user.Status != UserStatus.Active || parent.ExpiresAt <= now) return AuthResult.Reject(AuthError.InvalidCredentials);
        var keyHash = TokenCodec.Hash(requestKey);
        if (parent.ConsumedAt is not null)
        {
            if (parent.RetryUntil > now && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parent.RetryKeyHash ?? ""), Encoding.ASCII.GetBytes(keyHash)) && parent.EncryptedSuccessor is not null)
            {
                var child = await db.RefreshTokens.SingleAsync(x => x.ParentId == parent.Id, ct);
                if (child.ConsumedAt is not null || child.ExpiresAt <= now) return AuthResult.Reject(AuthError.InvalidCredentials);
                return new(codec.Unprotect(parent.EncryptedSuccessor, parent.Id), null);
            }
            lockedFamily.RevokedAt = now; parent.EncryptedSuccessor = null;
            // Security outcome must commit before returning 401; throwing would roll back revocation.
            await db.SaveChangesAsync(CancellationToken.None); await tx.CommitAsync(CancellationToken.None);
            return AuthResult.Reject(AuthError.InvalidCredentials);
        }
        var successor = TokenCodec.RandomToken(); var result = codec.Issue(user, successor, now);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), FamilyId = parent.FamilyId, ParentId = parent.Id,
            Hash = TokenCodec.Hash(successor), ExpiresAt = lockedFamily.ExpiresAt });
        parent.ConsumedAt = now; parent.RetryUntil = now.AddSeconds(15); parent.RetryKeyHash = keyHash;
        parent.EncryptedSuccessor = codec.Protect(result, parent.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(result, null);
    }
}
