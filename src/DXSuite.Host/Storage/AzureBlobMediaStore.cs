using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DXSuite.Shared.Storage;

namespace DXSuite.Host.Storage;

/// <summary>
/// IMediaStore implementation backed by Azure Blob Storage. Each tenant maps to its own container
/// (container name == tenant id) and a file's relative path is the blob name. This mirrors the
/// per-tenant database isolation: one storage account per environment, one container per tenant.
/// Selected when Storage:BlobEndpoint is configured; LocalDiskMediaStore is used otherwise.
///
/// Per-tenant dedicated accounts (for stronger isolation or data residency) would be added at
/// ResolveContainer, returning a container from a tenant-specific BlobServiceClient instead of the
/// shared one. The default below uses the single environment account injected via DI.
/// </summary>
public sealed class AzureBlobMediaStore : IMediaStore
{
    private readonly BlobServiceClient _service;
    private readonly ILogger<AzureBlobMediaStore> _logger;
    private readonly ConcurrentDictionary<string, BlobContainerClient> _containers = new();
    private readonly ConcurrentDictionary<string, bool> _ensured = new();

    public AzureBlobMediaStore(BlobServiceClient service, ILogger<AzureBlobMediaStore> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task<Stream?> OpenReadAsync(string tenantId, string relativePath, CancellationToken ct = default)
    {
        var blob = ResolveContainer(tenantId).GetBlobClient(ToBlobName(relativePath));
        try
        {
            return await blob.OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Missing blob or container resolves to "no file", matching the local-disk store.
            return null;
        }
    }

    public async Task SaveAsync(string tenantId, string relativePath, Stream content, CancellationToken ct = default)
    {
        var container = ResolveContainer(tenantId);
        await EnsureContainerAsync(container, ct);
        await container.GetBlobClient(ToBlobName(relativePath)).UploadAsync(content, overwrite: true, ct);
        _logger.LogDebug("Saved blob {Path} for tenant {TenantId}", relativePath, tenantId);
    }

    public async Task DeleteAsync(string tenantId, string relativePath, CancellationToken ct = default)
        => await ResolveContainer(tenantId).GetBlobClient(ToBlobName(relativePath))
            .DeleteIfExistsAsync(cancellationToken: ct);

    public async Task<IReadOnlyList<string>> ListAsync(string tenantId, string prefix = "", CancellationToken ct = default)
    {
        var container = ResolveContainer(tenantId);
        var p = ToBlobName(prefix);
        var results = new List<string>();
        // Blob names are already tenant-relative paths with forward slashes, matching the contract.
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, string.IsNullOrEmpty(p) ? null : p, ct))
            results.Add(item.Name);
        return results;
    }

    // Blob storage has no local filesystem path. Callers needing one (e.g. ffmpeg) must copy the
    // stream from OpenReadAsync to a temp file. Nothing consumes this in the codebase today.
    public string? GetLocalPath(string tenantId, string relativePath) => null;

    // Extension point for per-tenant dedicated accounts: resolve a container from a tenant-specific
    // BlobServiceClient here. Default: the shared environment account, one container per tenant.
    private BlobContainerClient ResolveContainer(string tenantId)
        => _containers.GetOrAdd(ToContainerName(tenantId), name => _service.GetBlobContainerClient(name));

    private async Task EnsureContainerAsync(BlobContainerClient container, CancellationToken ct)
    {
        if (_ensured.ContainsKey(container.Name)) return;
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _ensured[container.Name] = true;
    }

    private static string ToContainerName(string tenantId) => tenantId.ToLowerInvariant();

    private static string ToBlobName(string relativePath) => relativePath.Replace('\\', '/').TrimStart('/');
}
