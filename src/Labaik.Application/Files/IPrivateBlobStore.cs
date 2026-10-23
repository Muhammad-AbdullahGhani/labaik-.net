namespace Labaik.Application.Files;

public sealed record BlobGrant(Uri Url, DateTimeOffset ExpiresAt);
public sealed record BlobInfo(long Size, string ETag, string? VersionId, string ContentType);
public interface IPrivateBlobStore
{
    Task<BlobGrant> UploadAsync(string exactKey, CancellationToken ct);
    Task<BlobGrant> ReadAsync(string exactKey, string? versionId, CancellationToken ct);
    Task<BlobInfo> InspectAsync(string exactKey, CancellationToken ct);
    Task<bool> ReadyAsync(CancellationToken ct);
}
public sealed class FileStoreUnavailableException : Exception;
