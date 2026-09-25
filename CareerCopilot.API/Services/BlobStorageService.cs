using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CareerCopilot.API.Models;

namespace CareerCopilot.API.Services;

public record StoredDocument(
    string Name,
    long SizeBytes,
    DateTimeOffset? LastModified,
    DocumentKind Kind,
    string ContentType,
    string? SourceUrl);

public record DocumentUpload(string FileName, DocumentKind Kind, string ContentType, string? SourceUrl = null);

public record DownloadedDocument(Stream Content, string ContentType);

public interface IBlobStorageService
{
    Task UploadAsync(DocumentUpload upload, Stream content, CancellationToken ct = default);
    Task<DownloadedDocument?> DownloadAsync(string fileName, CancellationToken ct = default);
    Task<StoredDocument?> GetAsync(string fileName, CancellationToken ct = default);
    Task<IReadOnlyList<StoredDocument>> ListAsync(CancellationToken ct = default);
    Task<bool> DeleteAsync(string fileName, CancellationToken ct = default);
}

public class BlobStorageService : IBlobStorageService
{
    private const string KindMetadataKey = "kind";
    private const string SourceMetadataKey = "source";

    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public BlobStorageService(IConfiguration config)
    {
        var connectionString = config["BlobStorage:ConnectionString"]
            ?? throw new InvalidOperationException("Falta BlobStorage:ConnectionString en la configuración.");
        var containerName = config["BlobStorage:ContainerName"] ?? "career-docs";

        // Pocos reintentos: si Azurite/Storage no está levantado, la UI debe enterarse rápido.
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 2;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(15);

        _container = new BlobServiceClient(connectionString, options).GetBlobContainerClient(containerName);
    }

    public async Task UploadAsync(DocumentUpload upload, Stream content, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var metadata = new Dictionary<string, string> { [KindMetadataKey] = upload.Kind.ToString() };
        if (upload.SourceUrl is not null)
            metadata[SourceMetadataKey] = Uri.EscapeDataString(upload.SourceUrl); // la metadata solo admite ASCII

        await _container.GetBlobClient(upload.FileName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = upload.ContentType },
            Metadata = metadata
        }, ct);
    }

    public async Task<DownloadedDocument?> DownloadAsync(string fileName, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        try
        {
            var response = await _container.GetBlobClient(fileName).DownloadStreamingAsync(cancellationToken: ct);
            return new DownloadedDocument(response.Value.Content, response.Value.Details.ContentType ?? "application/pdf");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<StoredDocument?> GetAsync(string fileName, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        try
        {
            var props = (await _container.GetBlobClient(fileName).GetPropertiesAsync(cancellationToken: ct)).Value;
            return new StoredDocument(fileName, props.ContentLength, props.LastModified, ParseKind(props.Metadata),
                props.ContentType ?? "application/pdf", ParseSource(props.Metadata));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<StoredDocument>> ListAsync(CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var documents = new List<StoredDocument>();
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.Metadata, cancellationToken: ct))
        {
            documents.Add(new StoredDocument(
                blob.Name,
                blob.Properties.ContentLength ?? 0,
                blob.Properties.LastModified,
                ParseKind(blob.Metadata),
                blob.Properties.ContentType ?? "application/pdf",
                ParseSource(blob.Metadata)));
        }
        return documents;
    }

    public async Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var response = await _container.GetBlobClient(fileName).DeleteIfExistsAsync(cancellationToken: ct);
        return response.Value;
    }

    // El contenedor se crea en el primer uso (no en el constructor) para que la API
    // arranque aunque el Storage todavía no esté disponible.
    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static DocumentKind ParseKind(IDictionary<string, string>? metadata) =>
        metadata is not null
        && metadata.TryGetValue(KindMetadataKey, out var value)
        && Enum.TryParse<DocumentKind>(value, ignoreCase: true, out var kind)
            ? kind
            : DocumentKind.Other;

    private static string? ParseSource(IDictionary<string, string>? metadata) =>
        metadata is not null && metadata.TryGetValue(SourceMetadataKey, out var value)
            ? Uri.UnescapeDataString(value)
            : null;
}
