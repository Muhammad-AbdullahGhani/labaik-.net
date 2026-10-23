using Azure.Storage.Blobs;
using BlobInfo = Labaik.Application.Files.BlobInfo;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Labaik.Application.Files;

namespace Labaik.Infrastructure.Operations;

public sealed class AzurePrivateBlobStore : IPrivateBlobStore
{
    private readonly BlobContainerClient _container;
    public AzurePrivateBlobStore(string connection, string container)
    {
        _container = new BlobServiceClient(connection, new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03)).GetBlobContainerClient(container);
    }
    private async Task PrivateAsync(CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        if ((await _container.GetAccessPolicyAsync(cancellationToken: ct)).Value.BlobPublicAccess != PublicAccessType.None)
            throw new FileStoreUnavailableException();
    }
    public async Task<BlobGrant> UploadAsync(string exactKey, CancellationToken ct)
    {
        await PrivateAsync(ct); var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var sas = new BlobSasBuilder { BlobContainerName = _container.Name, BlobName = exactKey, Resource = "b", Protocol = _container.Uri.Scheme == "https" ? SasProtocol.Https : SasProtocol.HttpsAndHttp, ExpiresOn = expires };
        // Create (c), WITHOUT Write (w): the same URL cannot overwrite an existing blob, even before approval.
        sas.SetPermissions(BlobSasPermissions.Create);
        return new(_container.GetBlobClient(exactKey).GenerateSasUri(sas), expires);
    }
    public async Task<BlobGrant> ReadAsync(string exactKey, string? versionId, CancellationToken ct)
    {
        await PrivateAsync(ct); var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var blob = _container.GetBlobClient(exactKey); if (versionId is not null) blob = blob.WithVersion(versionId);
        var sas = new BlobSasBuilder { BlobContainerName = _container.Name, BlobName = exactKey, Resource = versionId is null ? "b" : "bv", BlobVersionId = versionId, Protocol = _container.Uri.Scheme == "https" ? SasProtocol.Https : SasProtocol.HttpsAndHttp, ExpiresOn = expires };
        sas.SetPermissions(BlobSasPermissions.Read); return new(blob.GenerateSasUri(sas), expires);
    }
    public async Task<BlobInfo> InspectAsync(string exactKey, CancellationToken ct)
    {
        var properties = (await _container.GetBlobClient(exactKey).GetPropertiesAsync(cancellationToken: ct)).Value;
        return new(properties.ContentLength, properties.ETag.ToString(), properties.VersionId, properties.ContentType);
    }
    public async Task<bool> ReadyAsync(CancellationToken ct)
    { await PrivateAsync(ct); return true; }
}
