using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Storage;

namespace DXSuite.Host.Storage;

/// <summary>
/// IMediaStore implementation backed by the local filesystem.
/// Each tenant's files live under its configured MediaRootPath (or wwwroot/tenants/{subdomain}
/// if no override is configured). This is the initial Phase 8 implementation; swap for
/// AzureBlobMediaStore when moving to cloud-hosted multi-tenant storage.
/// </summary>
public sealed class LocalDiskMediaStore : IMediaStore
{
    private readonly ITenantStore _tenantStore;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalDiskMediaStore> _logger;

    public LocalDiskMediaStore(
        ITenantStore tenantStore,
        IWebHostEnvironment env,
        ILogger<LocalDiskMediaStore> logger)
    {
        _tenantStore = tenantStore;
        _env = env;
        _logger = logger;
    }

    public async Task<Stream?> OpenReadAsync(string tenantId, string relativePath, CancellationToken ct = default)
    {
        var fullPath = await ResolveFullPathAsync(tenantId, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return null;
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
    }

    public async Task SaveAsync(string tenantId, string relativePath, Stream content, CancellationToken ct = default)
    {
        var fullPath = await ResolveFullPathAsync(tenantId, relativePath);
        if (fullPath == null) throw new InvalidOperationException($"Cannot resolve media root for tenant {tenantId}.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(fs, ct);
        _logger.LogDebug("Saved media file {Path} for tenant {TenantId}", relativePath, tenantId);
    }

    public async Task DeleteAsync(string tenantId, string relativePath, CancellationToken ct = default)
    {
        var fullPath = await ResolveFullPathAsync(tenantId, relativePath);
        if (fullPath != null && File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogDebug("Deleted media file {Path} for tenant {TenantId}", relativePath, tenantId);
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(string tenantId, string prefix = "", CancellationToken ct = default)
    {
        var root = await ResolveRootAsync(tenantId);
        if (root == null || !Directory.Exists(root)) return [];

        var searchRoot = string.IsNullOrEmpty(prefix)
            ? root
            : Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(searchRoot)) return [];

        return Directory
            .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();
    }

    public string? GetLocalPath(string tenantId, string relativePath)
    {
        // Synchronous path resolution via cached tenant store (in-memory, no I/O).
        var tenant = _tenantStore.FindBySubdomainAsync(tenantId).GetAwaiter().GetResult()
                  ?? _tenantStore.GetAllEnabledAsync().GetAwaiter().GetResult()
                                 .FirstOrDefault(t => t.TenantId == tenantId);

        if (tenant == null) return null;
        var root = ResolveRoot(tenant);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<string?> ResolveFullPathAsync(string tenantId, string relativePath)
    {
        var root = await ResolveRootAsync(tenantId);
        if (root == null) return null;

        // Guard against path traversal. Normalize root first so the prefix check is
        // unambiguous (e.g. "C:\media\acme" must not pass for "C:\media\acme-evil\").
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt: tenant {TenantId} relativePath {RelPath}", tenantId, relativePath);
            return null;
        }
        return full;
    }

    private async Task<string?> ResolveRootAsync(string tenantId)
    {
        var tenants = await _tenantStore.GetAllEnabledAsync();
        var tenant = tenants.FirstOrDefault(t => t.TenantId == tenantId);
        if (tenant == null) return null;
        return ResolveRoot(tenant);
    }

    private string ResolveRoot(DXSuite.Shared.Multitenancy.TenantRecord tenant)
        => tenant.MediaRootPath
           ?? Path.Combine(_env.WebRootPath, "tenants", tenant.Subdomain);
}
