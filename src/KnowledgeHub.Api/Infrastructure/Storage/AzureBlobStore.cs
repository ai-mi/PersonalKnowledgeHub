using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Infrastructure.Storage;

public sealed class AzureBlobStore(IOptions<StorageOptions> options) : IBlobStore
{
    // Клиент создаётся при первом обращении: приложение без хранилища (настройки не заданы) должно стартовать.
    private readonly Lazy<BlobServiceClient> _service = new(() => CreateClient(options.Value));

    private static BlobServiceClient CreateClient(StorageOptions o) =>
        !string.IsNullOrWhiteSpace(o.ConnectionString) ? new BlobServiceClient(o.ConnectionString)
        : !string.IsNullOrWhiteSpace(o.AccountUri) ? new BlobServiceClient(new Uri(o.AccountUri), new DefaultAzureCredential())
        : throw new InvalidOperationException("Blob storage is not configured: set Storage:ConnectionString or Storage:AccountUri.");

    private BlobClient Blob(string container, string path) => _service.Value.GetBlobContainerClient(container).GetBlobClient(path);

    public async Task EnsureContainersAsync(CancellationToken ct)
    {
        foreach (var name in BlobContainers.All)
        {
            // Доступ по умолчанию приватный.
            await _service.Value.GetBlobContainerClient(name).CreateIfNotExistsAsync(cancellationToken: ct);
        }
    }

    public Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken ct) =>
        Blob(container, path).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);

    public async Task<Stream?> OpenReadAsync(string container, string path, CancellationToken ct)
    {
        try
        {
            return await Blob(container, path).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public Task<Stream> OpenWriteAsync(string container, string path, string contentType, CancellationToken ct) =>
        _service.Value.GetBlobContainerClient(container).GetBlockBlobClient(path).OpenWriteAsync(
            overwrite: true, new BlockBlobOpenWriteOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);

    public async Task DeleteAsync(string container, string path, CancellationToken ct) =>
        await Blob(container, path).DeleteIfExistsAsync(cancellationToken: ct);
}
