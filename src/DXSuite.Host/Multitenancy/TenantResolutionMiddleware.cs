using System.Security.Cryptography;
using System.Text;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Options;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Resolves the current tenant from the request's Host header subdomain and populates
/// the scoped TenantContext. Must run before UseAuthentication() and UseAuthorization().
///
/// Subdomain extraction: "acme.dxsuite.cloud" → "acme".
/// Localhost / no-subdomain requests fall back to DXSuite:DefaultTenant.
/// API key requests are not resolved here; Layer 7 (API key binding) handles those.
/// </summary>
internal sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenantResolutionOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    private readonly TenantResolutionOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, ITenantStore store, TenantContext tenantContext,
        TenantSubdomainState subdomainState)
    {
        var subdomain = ExtractSubdomain(context.Request.Host.Host);

        TenantRecord? record;
        if (subdomain is not null)
        {
            record = await store.FindBySubdomainAsync(subdomain, context.RequestAborted);

            // An unrecognized subdomain is an explicit 404 — do NOT silently route to the
            // default tenant. A DNS typo or unknown customer subdomain must not silently
            // expose another tenant's data.
            if (record is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"Tenant '{subdomain}' not found.");
                return;
            }
        }
        else
        {
            // No subdomain (localhost, apex domain) — fall back to DXSuite:DefaultTenant.
            record = await store.GetDefaultAsync(context.RequestAborted);
            if (record is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync(
                    "No tenant is configured for this request. " +
                    "Ensure at least one tenant is defined in appsettings.json.");
                return;
            }

            // X-API-KEY can override the default tenant on no-subdomain requests (e.g. localhost
            // API calls or worker-tier clients that authenticate with an API key rather than a
            // subdomain). Subdomain requests are already tenant-specific and do not need this.
            //
            // Off unless a deployment opts in. This middleware runs before authentication and
            // before endpoint selection, so an anonymous route cannot decline the header for
            // itself: the lookup has already happened by the time a handler exists. The header is
            // also not ours to trust — BrightSign OS sets one of its own on every recovery poll —
            // and on ConfigTenantStore the lookup opens a connection to every configured tenant's
            // Director database in turn, which makes an unauthenticated request amplify.
            if (_options.AllowApiKeyTenantOverride)
            {
                var apiKey = context.Request.Headers["X-API-KEY"].FirstOrDefault();

                // Device tokens travel in this same header on every player poll (DeviceTokenAuthHandler)
                // and are never tenant API keys, so they would only ever scan and miss. Players carry
                // their tenant in the subdomain instead.
                if (apiKey is not null && !apiKey.StartsWith("dt_", StringComparison.Ordinal))
                {
                    var keyHash = ComputeApiKeyHash(apiKey);
                    var tenantByKey = await store.FindTenantByApiKeyHashAsync(keyHash, context.RequestAborted);
                    if (tenantByKey is not null)
                    {
                        logger.LogInformation(
                            "X-API-KEY overrode the default tenant '{DefaultTenant}' with '{OverrideTenant}' for {Method} {Path}.",
                            record.Subdomain, tenantByKey.Subdomain, context.Request.Method, context.Request.Path);
                        record = tenantByKey;
                    }
                }
            }
        }

        tenantContext.Populate(record);

        // Stash the resolved subdomain so TenantCircuitInitializer can repopulate
        // TenantContext in the Blazor Server circuit scope, which is a separate DI
        // scope that never flows through this middleware.
        subdomainState.Subdomain = record.Subdomain;

        await next(context);
    }

    private static string ComputeApiKeyHash(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(bytes);
    }

    private static string? ExtractSubdomain(string host)
    {
        // Strip port (e.g. "acme.localtest.me:5298" → "acme.localtest.me")
        var hostOnly = host.Contains(':') ? host[..host.IndexOf(':')] : host;

        // Bare IP addresses have no subdomain — fall back to the default tenant.
        // This covers direct-IP access (e.g. Pixera CEF or local dev via IP).
        if (System.Net.IPAddress.TryParse(hostOnly, out _))
            return null;

        var parts = hostOnly.Split('.');

        // Need at least subdomain.domain.tld to have a real subdomain
        if (parts.Length < 3)
            return null;

        var subdomain = parts[0];

        // "www" is not a tenant subdomain
        return string.Equals(subdomain, "www", StringComparison.OrdinalIgnoreCase) ? null : subdomain;
    }
}
