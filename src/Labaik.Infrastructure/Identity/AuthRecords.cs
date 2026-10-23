namespace Labaik.Infrastructure.Identity;

// Technical persistence records are only mutated by the transactional identity service, never bound from HTTP.
public sealed class RefreshFamily
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Hash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RetryUntil { get; set; }
    public string? RetryKeyHash { get; set; }
    public string? EncryptedSuccessor { get; set; }
}
public sealed class AuthRateBucket
{
    public string Key { get; set; } = null!;
    public DateTimeOffset WindowStart { get; set; }
    public int Count { get; set; }
}
