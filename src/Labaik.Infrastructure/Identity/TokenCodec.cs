using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Labaik.Application.Identity;
using Labaik.Domain.Users;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Labaik.Infrastructure.Identity;

public sealed class TokenCodec(AuthSettings settings)
{
    public static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public string RateKey(string subject) => Convert.ToHexString(HMACSHA256.HashData(settings.RecoveryKey, Encoding.UTF8.GetBytes("rate:" + subject)));
    public AuthTokens Issue(User user, string refreshToken, DateTimeOffset now)
    {
        var expires = now.AddMinutes(10);
        var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer, Audience = settings.Audience, IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime, SigningCredentials = new(new SymmetricSecurityKey(settings.SigningKey), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = user.Id.ToString(), ["role"] = user.Role.ToString(), ["jti"] = Guid.NewGuid().ToString("N") }
        });
        return new(jwt, refreshToken, expires);
    }
    public string Protect(AuthTokens tokens, Guid parentId)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
        using var aes = new AesGcm(settings.RecoveryKey, 16);
        aes.Encrypt(nonce, plain, cipher, tag, parentId.ToByteArray());
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public AuthTokens Unprotect(string value, Guid parentId)
    {
        var envelope = Convert.FromBase64String(value); var plain = new byte[envelope.Length - 28];
        using var aes = new AesGcm(settings.RecoveryKey, 16);
        aes.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plain, parentId.ToByteArray());
        return JsonSerializer.Deserialize<AuthTokens>(plain)!;
    }
}
