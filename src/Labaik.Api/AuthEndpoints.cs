using Labaik.Application.Identity;
using Labaik.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Npgsql;

namespace Labaik.Api;

public sealed record FirebaseLoginRequest(string IdToken);
public sealed record RefreshRequest(string RefreshToken, string RequestKey);

internal static class AuthEndpoints
{
    public static void AddIdentity(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32768);
        builder.Services.AddSingleton<AuthSettings>();
        builder.Services.AddSingleton<TokenCodec>();
        builder.Services.AddSingleton<IFirebaseTokenVerifier, FirebaseTokenVerifier>();
        builder.Services.AddScoped<AuthRateLimiter>();
        builder.Services.AddScoped<IdentityService>();
        builder.Services.AddScoped<VerifiedUidProvisioner>();
        builder.Services.AddHostedService<AuthCleanupWorker>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            try { options.TokenValidationParameters = new AuthSettings(builder.Configuration).ValidationParameters(); }
            catch (AuthUnavailableException)
            {
                // Keep health available without optional auth configuration; token validation fails closed.
                options.TokenValidationParameters = new() { RequireSignedTokens = true, ValidateIssuerSigningKey = true };
            }
        });
        builder.Services.AddAuthorization();
    }
    public static void Map(WebApplication app)
    {
        app.UseAuthentication(); app.UseAuthorization();
        app.MapPost("/auth/firebase", async (FirebaseLoginRequest request, HttpContext context) =>
        {
            if (request.IdToken is not { Length: > 0 and <= 16384 }) return Results.BadRequest();
            return await Run(context, service => service.LoginAsync(request.IdToken, Ip(context), context.RequestAborted));
        });
        app.MapPost("/auth/refresh", async (RefreshRequest request, HttpContext context) =>
        {
            if (request.RefreshToken is not { Length: 64 } || !IdentityService.IsRequestKey(request.RequestKey)) return Results.BadRequest();
            return await Run(context, service => service.RefreshAsync(request.RefreshToken, request.RequestKey, Ip(context), context.RequestAborted));
        });
        app.MapGet("/auth/me", (HttpContext context) => Results.Ok(new
        {
            UserId = context.User.FindFirst("sub")?.Value, Role = context.User.FindFirst("role")?.Value
        })).RequireAuthorization();
    }
    private static string Ip(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static async Task<IResult> Run(HttpContext context, Func<IdentityService, Task<AuthResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        try
        {
            var result = await action(context.RequestServices.GetRequiredService<IdentityService>());
            return result.Error switch
            {
                AuthError.InvalidCredentials => Results.Unauthorized(),
                AuthError.RateLimited => Results.StatusCode(429),
                _ => Results.Ok(result.Tokens)
            };
        }
        catch (Exception error) when (error is AuthUnavailableException or NpgsqlException or TimeoutException)
        {
            // Authentication must fail closed; expose only diagnostic categories in logs, never credentials.
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Labaik.Auth")
                .LogWarning("Authentication unavailable: {ErrorType}, SQL state {SqlState}, origin {Origin}", error.GetType().Name,
                    (error as PostgresException)?.SqlState, error.TargetSite?.DeclaringType?.FullName);
            return Results.StatusCode(503);
        }
    }
}
