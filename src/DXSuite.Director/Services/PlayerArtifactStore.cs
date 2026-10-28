using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Where player artifacts live on disk: a tenant-keyed root <b>outside the web root</b>, so no
/// artifact is reachable by static-file middleware and no tenant can overwrite another's bytes.
///
/// Both problems were real rather than theoretical. Uploads built their path from the global web
/// root while the duplicate-version check ran against the tenant's own database, so on a hosted
/// Director one tenant uploading a version number another already had silently replaced that
/// tenant's zip, after which its recorded hash no longer matched the bytes on disk.
/// </summary>
public interface IPlayerArtifactStore
{
    /// <summary>Absolute root for this tenant's artifacts. Created on demand.</summary>
    string GetRoot();

    /// <summary>Writes an artifact and returns the stored path to record on the version row.</summary>
    Task<string> SaveAsync(Stream content, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Absolute path for a stored path, or null when the artifact is not on disk.
    ///
    /// Falls back to the web root for rows written before the store existed. That path is read
    /// only: nothing is ever written back there, so the anonymous surface shrinks to the artifacts
    /// that already existed rather than being inherited by every new one.
    /// </summary>
    string? ResolveExisting(string storedPath);

    void Delete(string storedPath);
}

public sealed class PlayerArtifactStore : IPlayerArtifactStore
{
    private readonly ITenantContext _tenantContext;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<PlayerArtifactStore> _logger;

    public PlayerArtifactStore(
        ITenantContext tenantContext, IWebHostEnvironment env, IConfiguration config,
        ILogger<PlayerArtifactStore> logger)
    {
        _tenantContext = tenantContext;
        _env = env;
        _config = config;
        _logger = logger;
    }

    public string GetRoot()
    {
        // Outside ContentRoot/wwwroot by construction: the default sits beside the content root
        // rather than under it, so enabling static files over the app directory cannot expose it.
        var configured = _config["DXSuite:PlayerArtifacts:Root"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_env.ContentRootPath, "PlayerArtifacts")
            : configured;

        if (_tenantContext.IsPopulated && !string.IsNullOrWhiteSpace(_tenantContext.Subdomain))
            root = Path.Combine(root, _tenantContext.Subdomain);

        return Path.IsPathRooted(root) ? root : Path.Combine(_env.ContentRootPath, root);
    }

    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken cancellationToken = default)
    {
        var normalised = Normalise(relativePath);
        var absolute = Path.Combine(GetRoot(), normalised.Replace('/', Path.DirectorySeparatorChar));

        var directory = Path.GetDirectoryName(absolute);
        if (directory != null)
            Directory.CreateDirectory(directory);

        await using (var file = new FileStream(absolute, FileMode.Create))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return normalised;
    }

    public string? ResolveExisting(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        var normalised = Normalise(storedPath);
        var candidate = Path.Combine(GetRoot(), normalised.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(candidate))
            return candidate;

        // Pre-store rows recorded a path relative to the web root.
        var legacy = Path.Combine(_env.WebRootPath, normalised.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(legacy))
        {
            _logger.LogDebug("Artifact {StoredPath} served from the legacy web-root location", storedPath);
            return legacy;
        }

        return null;
    }

    public void Delete(string storedPath)
    {
        var absolute = ResolveExisting(storedPath);
        if (absolute == null)
            return;

        File.Delete(absolute);

        var directory = Path.GetDirectoryName(absolute);
        if (directory != null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

    /// <summary>
    /// Rejects anything that would climb out of the tenant's root. Stored paths come from the
    /// database, so this guards a value an earlier defect could have written rather than one a
    /// caller supplies today.
    /// </summary>
    private static string Normalise(string relativePath)
    {
        var normalised = relativePath.Replace('\\', '/').TrimStart('/');

        if (normalised.Split('/').Any(segment => segment == ".."))
            throw new InvalidOperationException($"Artifact path '{relativePath}' escapes the artifact root.");

        return normalised;
    }
}
