using System.Collections.Concurrent;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Rewrites requests for tenant-owned content (see TenantMediaPaths.TenantContentPrefixes)
/// to the resolved tenant's media root before static-file serving, so each tenant is served
/// its own files while stored AssetPath values, device manifests, and img tags keep their
/// existing URL shape. Must run after tenant resolution and before UseStaticFiles.
///
/// Direct requests into /tenants/** are rejected: that namespace is reachable only through
/// the rewrite, so one tenant cannot fetch another tenant's files by crafting a URL.
/// </summary>
internal sealed class TenantMediaMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TenantMediaMiddleware> _logger;
    private readonly bool _enabled;
    private readonly bool _globalFallback;

    // Roots we've already warned about, so an unservable override logs once instead of per request.
    private readonly ConcurrentDictionary<string, byte> _warnedRoots = new();

    // One entry per tenant root + content prefix once its state is known, so the strict-mode
    // existence probe and its warning happen once per prefix rather than on every request.
    private readonly ConcurrentDictionary<string, byte> _checkedPrefixes = new();

    public TenantMediaMiddleware(
        RequestDelegate next,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<TenantMediaMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
        _enabled = TenantMediaPaths.IsEnabled(config);
        // When the tenant copy of a file is missing, fall through to the global web root so
        // pre-migration media keeps serving. Set to false once existing media has been moved
        // into tenant roots to make isolation strict.
        _globalFallback = config.GetValue("DXSuite:TenantMedia:GlobalFallback", true);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Isolation off (single-tenant installs): full pass-through, identical to pre-DXME-48.
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;

        if (path.StartsWithSegments("/tenants"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && MatchesTenantPrefix(path))
        {
            var tenantCtx = context.RequestServices.GetService<ITenantContext>();
            if (tenantCtx?.IsPopulated == true)
            {
                var root = TenantMediaPaths.GetRoot(tenantCtx, _env);
                if (!PathsEqual(root, _env.WebRootPath))
                    RewriteToTenantRoot(context, root);
            }
        }

        await _next(context);
    }

    private static bool MatchesTenantPrefix(PathString path)
    {
        foreach (var prefix in TenantMediaPaths.TenantContentPrefixes)
        {
            if (path.StartsWithSegments(prefix))
                return true;
        }
        return false;
    }

    private void RewriteToTenantRoot(HttpContext context, string root)
    {
        // Static-file middleware can only serve content under the web root, so the tenant
        // root must live inside it (the "wwwroot/tenants/{subdomain}" default does).
        var relRoot = Path.GetRelativePath(_env.WebRootPath, root);
        if (relRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relRoot))
        {
            if (_warnedRoots.TryAdd(root, 0))
                _logger.LogWarning(
                    "Tenant media root {Root} is outside the web root and cannot be served " +
                    "statically; requests will fall back to global content", root);
            return;
        }

        var requestPath = context.Request.Path.Value!;
        var physical = Path.GetFullPath(Path.Combine(
            root, requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        // Containment guard: anything that escapes the tenant root is treated as not found.
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        if (!physical.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return;

        // With fallback enabled a missing tenant file leaves the path untouched so the global
        // copy serves; in strict mode the rewrite happens regardless and the miss 404s rather
        // than silently serving another tenant's file from the global folder.
        if (_globalFallback && !File.Exists(physical))
            return;

        if (!_globalFallback)
            WarnOnceIfPrefixUnpopulated(root, requestPath, physical);

        context.Request.Path = new PathString("/" + relRoot.Replace('\\', '/')).Add(context.Request.Path);
    }

    /// <summary>
    /// In strict mode a tenant root that was never populated turns every request under a content
    /// prefix into a bare 404 with nothing in the logs tying it to the rewrite. Probe the first
    /// request per tenant root + prefix and name the remedy, so an environment enabled without
    /// running the media migration is diagnosable from the log rather than from the URL alone.
    /// </summary>
    private void WarnOnceIfPrefixUnpopulated(string root, string requestPath, string physical)
    {
        var prefix = TenantMediaPaths.TenantContentPrefixes.FirstOrDefault(
            p => requestPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (prefix is null)
            return;

        var key = root + "|" + prefix;
        if (_checkedPrefixes.ContainsKey(key) || File.Exists(physical))
        {
            _checkedPrefixes.TryAdd(key, 0);
            return;
        }

        if (_checkedPrefixes.TryAdd(key, 0))
            _logger.LogWarning(
                "Tenant media is strict (GlobalFallback=false) but {Prefix} under tenant root {Root} " +
                "has no file for {RequestPath} (expected {Physical}); requests will 404. Run " +
                "deploy/migrate-media-to-tenant-root.ps1 for this tenant, point its FilePaths_MediaRoot " +
                "at 'wwwroot', or re-enable DXSuite:TenantMedia:GlobalFallback",
                prefix, root, requestPath, physical);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
