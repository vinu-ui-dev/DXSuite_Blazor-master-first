using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace DXSuite.Director.Services;

/// <summary>
/// The base URL a player should call back on.
///
/// One resolver rather than the inline <c>Request.Scheme</c>/<c>Host</c> rebuilds scattered across
/// the controllers, because two callers cannot use a request at all: provisioning package
/// generation writes an endpoint into a card that boots somewhere else entirely, and background
/// work has no request to read.
/// </summary>
public interface IPlayerCallbackUrlResolver
{
    /// <summary>
    /// Resolved, in order: the current request, then the tenant's configured site URL, then the
    /// global fallback. Returns an empty string when nothing is configured, which is the same
    /// answer the inline rebuilds gave and is caught by the callers that cannot tolerate it.
    /// </summary>
    string Resolve();

    /// <summary>
    /// The URL to write into a provisioning card, which deliberately never uses the request: the
    /// operator generating a package is on whatever host they happened to log in to, and the card
    /// boots at a customer site months later.
    /// </summary>
    string ResolveForProvisioning();
}

public sealed class PlayerCallbackUrlResolver : IPlayerCallbackUrlResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _config;

    public PlayerCallbackUrlResolver(
        IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext, IConfiguration config)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _config = config;
    }

    public string Resolve()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null)
            return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');

        return ResolveForProvisioning();
    }

    public string ResolveForProvisioning()
    {
        var tenantUrl = _tenantContext.IsPopulated ? _tenantContext.SiteUrl : null;
        var configured = tenantUrl ?? _config["FilePaths:SiteUrl"] ?? "";
        return configured.TrimEnd('/');
    }
}
