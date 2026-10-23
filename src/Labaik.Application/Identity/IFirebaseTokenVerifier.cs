namespace Labaik.Application.Identity;

// Only a verifier may produce this identity; callers never send a local user ID, role or email to select an account.
public sealed record VerifiedFirebaseIdentity(string Project, string Uid, string? Email = null);
public interface IFirebaseTokenVerifier
{
    Task<VerifiedFirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}
public sealed class InvalidFirebaseTokenException : Exception;
public sealed class AuthUnavailableException : Exception;
public sealed record AuthTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);
public enum AuthError { InvalidCredentials, RateLimited }
public sealed record AuthResult(AuthTokens? Tokens, AuthError? Error)
{
    public static AuthResult Reject(AuthError error) => new(null, error);
}
