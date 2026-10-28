using Microsoft.AspNetCore.Components.Server.Circuits;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Populates TenantContext when a Blazor Server interactive circuit opens.
/// TenantResolutionMiddleware handles HTTP scopes; this handles circuit scopes,
/// which are separate DI scopes not touched by any middleware.
///
/// The subdomain is read from the SignalR upgrade request's Host header via
/// IHttpContextAccessor — the WebSocket upgrade goes to the same domain as the
/// page load, so the Host is always the correct tenant domain regardless of DI
/// scope boundaries. TenantSubdomainState (scoped to the HTTP scope) is used as
/// a fallback for edge cases where HttpContext is unavailable.
/// </summary>
internal sealed class TenantCircuitInitializer(
    IPopulatableTenantContext tenantContext,
    ITenantStore store,
    TenantSubdomainState subdomainState,
    IHttpContextAccessor httpContextAccessor) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        if (((TenantContext)tenantContext).IsPopulated) return;

        // Read the Host header from the SignalR upgrade request. This is always the
        // correct tenant domain because the WebSocket connects to the same origin as
        // the page. IHttpContextAccessor is populated during circuit setup even though
        // there is no middleware pipeline at this point.
        var host = httpContextAccessor.HttpContext?.Request.Host.Host;
        var subdomain = ExtractSubdomain(host) ?? subdomainState.Subdomain;

        TenantRecord? record = null;
        if (!string.IsNullOrEmpty(subdomain))
            record = await store.FindBySubdomainAsync(subdomain, ct);

        record ??= await store.GetDefaultAsync(ct);

        if (record != null)
            tenantContext.Populate(record);
    }

    /// <summary>
    /// Mirrors the subdomain extraction logic in TenantResolutionMiddleware.
    /// Extracts the leftmost label from a hostname with at least three parts.
    /// </summary>
    private static string? ExtractSubdomain(string? host)
    {
        if (string.IsNullOrEmpty(host)) return null;
        if (System.Net.IPAddress.TryParse(host, out _)) return null;

        var parts = host.Split('.');
        if (parts.Length < 3) return null;

        var subdomain = parts[0];
        return string.Equals(subdomain, "www", StringComparison.OrdinalIgnoreCase) ? null : subdomain;
    }
}
