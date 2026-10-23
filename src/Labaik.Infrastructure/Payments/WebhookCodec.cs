using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Labaik.Application.Payments;

namespace Labaik.Infrastructure.Payments;

public sealed class WebhookCodec
{
    public const int MaxBodyBytes = 65536;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _signatureKey;
    private static readonly byte[] Aad = Encoding.ASCII.GetBytes("labaik.gateway.webhook.v1");
    public WebhookCodec(byte[] encryptionKey, byte[] signatureKey)
    {
        if (encryptionKey.Length != 32 || signatureKey.Length != 32 || encryptionKey.SequenceEqual(signatureKey))
            throw new ArgumentException("Independent 256-bit webhook keys required.");
        _encryptionKey = encryptionKey.ToArray(); _signatureKey = signatureKey.ToArray();
    }
    public byte[] Encrypt(PaymentNotice notice)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(notice);
        var body = new byte[29 + plain.Length]; body[0] = 1;
        RandomNumberGenerator.Fill(body.AsSpan(1, 12));
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(body.AsSpan(1, 12), plain, body.AsSpan(29), body.AsSpan(13, 16), Aad);
        return body;
    }
    public string Sign(byte[] body) => Convert.ToHexString(HMACSHA256.HashData(_signatureKey, body));
    public PaymentNotice VerifyAndDecrypt(byte[] body, string signature)
    {
        // Authenticate raw bytes before parsing/decrypting. No provider payload/exception text is logged.
        if (body.Length is < 30 or > MaxBodyBytes || signature.Length != 64 || !signature.All(char.IsAsciiHexDigit))
            throw new CryptographicException("Invalid webhook.");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sign(body)), Convert.FromHexString(signature)))
            throw new CryptographicException("Invalid webhook.");
        if (body[0] != 1) throw new CryptographicException("Unsupported webhook version.");
        var plain = new byte[body.Length - 29];
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(body.AsSpan(1, 12), body.AsSpan(29), body.AsSpan(13, 16), plain, Aad);
        var notice = JsonSerializer.Deserialize<PaymentNotice>(plain) ?? throw new JsonException();
        if (notice.EventId == Guid.Empty || notice.CheckoutId == Guid.Empty || notice.BookingId == Guid.Empty ||
            string.IsNullOrWhiteSpace(notice.IdempotencyKey) || notice.IdempotencyKey.Length > 128 ||
            notice.AmountMinor <= 0 || notice.Currency is not { Length: 3 } || notice.Status is not ("Pending" or "Succeeded" or "Failed"))
            throw new JsonException("Invalid webhook fields.");
        return notice;
    }
}
