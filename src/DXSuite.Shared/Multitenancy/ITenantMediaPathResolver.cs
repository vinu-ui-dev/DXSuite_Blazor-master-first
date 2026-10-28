using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace DXSuite.Shared.Multitenancy;

/// <summary>
/// Resolves the absolute disk root for the current tenant's web-servable content
/// (MediaAssets, override content, location images, visual backgrounds, models).
/// Use this instead of IWebHostEnvironment.WebRootPath when building paths to
/// tenant-owned content so tenants sharing a host cannot collide on disk.
/// </summary>
public interface ITenantMediaPathResolver
{
    /// <summary>
    /// Absolute root directory for the current tenant's media. Mirrors the wwwroot layout
    /// (tenant content lives under "{root}\MediaAssets\...", "{root}\images\...", etc.).
    /// Falls back to the global web root when no tenant is resolved in the current scope,
    /// preserving single-tenant behaviour.
    /// </summary>
    string GetRoot();
}

public sealed class TenantMediaPathResolver : ITenantMediaPathResolver
{
    private readonly ITenantContext _tenantContext;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public TenantMediaPathResolver(ITenantContext tenantContext, IWebHostEnvironment env, IConfiguration config)
    {
        _tenantContext = tenantContext;
        _env = env;
        _config = config;
    }

    public string GetRoot() => TenantMediaPaths.GetRoot(_tenantContext, _env, _config);
}

/// <summary>
/// Static core of tenant media root resolution, callable from background services that
/// hold a per-tenant scope's ITenantContext directly (IWorkerScopeFactory iteration).
/// </summary>
public static class TenantMediaPaths
{
    /// <summary>
    /// URL path prefixes whose files are tenant-owned content. Requests under these prefixes
    /// are rewritten to the tenant's media root by the Host's tenant media middleware, and
    /// disk writers must place files for these prefixes under GetRoot().
    /// </summary>
    public static readonly string[] TenantContentPrefixes =
    [
        "/MediaAssets",
        "/TempFiles",
        "/images/Location Images",
        "/images/Visual Backgrounds",
        "/models",
    ];

    /// <summary>
    /// Master switch for per-tenant media roots. Default OFF so single-tenant installs keep
    /// today's global wwwroot layout with no upgrade action; multi-tenant deployments enable
    /// it explicitly alongside the one-time media migration (see deploy/migrate-media-to-tenant-root.ps1).
    /// </summary>
    public static bool IsEnabled(IConfiguration config) =>
        config.GetValue("DXSuite:TenantMedia:Enabled", false);

    public static string GetRoot(ITenantContext? tenantContext, IWebHostEnvironment env, IConfiguration config) =>
        IsEnabled(config) ? GetRoot(tenantContext, env) : env.WebRootPath;

    /// <summary>
    /// Record-based resolution for singletons that look tenants up in ITenantStore rather
    /// than holding a scoped ITenantContext (caches, cleanup sweeps).
    /// </summary>
    public static string GetRoot(TenantRecord tenant, IWebHostEnvironment env, IConfiguration config)
    {
        if (!IsEnabled(config))
            return env.WebRootPath;

        var root = tenant.MediaRootPath ?? Path.Combine("wwwroot", "tenants", tenant.Subdomain);
        return Path.IsPathRooted(root) ? root : Path.Combine(env.ContentRootPath, root);
    }

    /// <summary>Flag-independent core; use the IConfiguration overload everywhere outside tests.</summary>
    public static string GetRoot(ITenantContext? tenantContext, IWebHostEnvironment env)
    {
        if (tenantContext?.IsPopulated != true)
            return env.WebRootPath;

        // MediaRootPath is either the tenant's FilePaths_MediaRoot override or the
        // "wwwroot/tenants/{subdomain}" default. Relative values resolve against the content
        // root, so an operator can point a legacy tenant at "wwwroot" to keep its existing
        // global media in place without moving files.
        var root = tenantContext.MediaRootPath;
        return Path.IsPathRooted(root) ? root : Path.Combine(env.ContentRootPath, root);
    }
}
