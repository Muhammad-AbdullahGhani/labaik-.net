using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Labaik.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Labaik.Infrastructure.Identity;

public sealed class FirebaseTokenVerifier(IConfiguration config, Google.Apis.Http.HttpClientFactory? httpClientFactory = null, ILogger<FirebaseTokenVerifier>? logger = null) : IFirebaseTokenVerifier, IDisposable
{
    private readonly Lazy<FirebaseApp> _app = new(() =>
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST")))
            throw new AuthUnavailableException(); // Emulator verification bypasses production signature/revocation guarantees.
        var project = config["Firebase:ProjectId"];
        var json = config["Firebase:CredentialsJson"];
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(json)) throw new AuthUnavailableException();
        // Credentials are trusted administrator configuration, never a request-supplied JSON document.
        var stage = "CredentialLoading";
        try
        {
            var credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
            if (httpClientFactory is not null) credential = credential.CreateWithHttpClientFactory(httpClientFactory);
            stage = "AppCreation";
            var options = new AppOptions { ProjectId = project, Credential = credential };
            // Keep the SDK's default factory in production. Its setter rejects null; tests supply a controlled factory.
            if (httpClientFactory is not null) options.HttpClientFactory = httpClientFactory;
            return FirebaseApp.Create(options, Guid.NewGuid().ToString("N"));
        }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException)
        {
            logger?.LogWarning("Firebase initialization unavailable: {Stage}, {ErrorType}, parameter {Parameter}",
                stage, error.GetType().Name, (error as ArgumentException)?.ParamName);
            throw new AuthUnavailableException();
        }
    });
    public async Task<VerifiedFirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await FirebaseAuth.GetAuth(_app.Value).VerifyIdTokenAsync(idToken, true, cancellationToken);
            // Admin SDK allows clock skew; do not exchange an already expired token for a fresh local session.
            if (token.Audience != config["Firebase:ProjectId"] || token.ExpirationTimeSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                throw new InvalidFirebaseTokenException();
            // Admin SDK validates signature/issuer; true also checks revocation/disabled user.
            return new(config["Firebase:ProjectId"]!, token.Uid);
        }
        catch (FirebaseAuthException error) when (error.AuthErrorCode is AuthErrorCode.ExpiredIdToken or AuthErrorCode.RevokedIdToken
            or AuthErrorCode.InvalidIdToken or AuthErrorCode.UserNotFound)
        { throw new InvalidFirebaseTokenException(); }
        catch (ArgumentException) { throw new InvalidFirebaseTokenException(); }
        catch (FirebaseException error)
        {
            // Log only SDK error enums, never the exception message, user identity, token or provider response.
            logger?.LogWarning("Firebase verification unavailable: {ErrorCode}, {AuthErrorCode}",
                error.ErrorCode, (error as FirebaseAuthException)?.AuthErrorCode);
            throw new AuthUnavailableException();
        }
    }
    public void Dispose() { if (_app.IsValueCreated) _app.Value.Delete(); }
}
