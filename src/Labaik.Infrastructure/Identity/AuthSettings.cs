using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Labaik.Infrastructure.Identity;

public sealed class AuthSettings
{
    public string FirebaseProject { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public byte[] SigningKey { get; }
    public byte[] RecoveryKey { get; }
    public int IpLimit { get; }
    public int AccountLimit { get; }
    public AuthSettings(IConfiguration config)
    {
        string Required(string name) => config[name] is { Length: > 0 } value ? value : throw new Application.Identity.AuthUnavailableException();
        FirebaseProject = Required("Firebase:ProjectId"); Issuer = Required("Auth:Issuer"); Audience = Required("Auth:Audience");
        try { SigningKey = Convert.FromBase64String(Required("Auth:SigningKey")); RecoveryKey = Convert.FromBase64String(Required("Auth:RecoveryKey")); }
        catch (FormatException) { throw new Application.Identity.AuthUnavailableException(); }
        if (SigningKey.Length != 32 || RecoveryKey.Length != 32 || SigningKey.SequenceEqual(RecoveryKey))
            throw new Application.Identity.AuthUnavailableException();
        IpLimit = config.GetValue("Auth:IpLimit", 60); AccountLimit = config.GetValue("Auth:AccountLimit", 30);
        if (IpLimit <= 0 || AccountLimit <= 0) throw new Application.Identity.AuthUnavailableException();
    }
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = Audience,
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(SigningKey),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero, NameClaimType = "sub", RoleClaimType = "role"
    };
}
