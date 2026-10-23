using Labaik.Domain.Bookings;

namespace Labaik.Domain.Users;

public enum UserStatus { Active, Disabled }
public sealed class User
{
    public Guid Id { get; private set; }
    public string FirebaseProject { get; private set; } = null!;
    public string FirebaseUid { get; private set; } = null!;
    public ActorRole Role { get; private set; }
    public UserStatus Status { get; private set; } = UserStatus.Active;
    public string Locale { get; private set; } = "en";
    public DateTimeOffset? SuspendedAt { get; private set; }
    public long LastOfferedSequence { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private User() { }
    public User(Guid id, string firebaseProject, string firebaseUid, ActorRole role, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(firebaseProject) || firebaseProject.Length > 128 ||
            string.IsNullOrWhiteSpace(firebaseUid) || firebaseUid.Length > 128)
            throw new ArgumentException("Invalid user identity.");
        if (!Enum.IsDefined(role) || role == ActorRole.System) throw new ArgumentException("System is not a user role.");
        Id = id; FirebaseProject = firebaseProject; FirebaseUid = firebaseUid; Role = role; CreatedAt = createdAt.ToUniversalTime();
    }
}
