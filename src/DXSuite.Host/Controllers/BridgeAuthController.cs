using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DXSuite.Host.Bridge;
using DXSuite.Host.Logging;
using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Models;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Controllers;

/// <summary>
/// Receives the Editor bridge handoff from Director. Director's Angular app already holds a
/// Director-user JWT and the PKCE verifier (it generated the challenge for /launch itself), so it
/// calls <see cref="Exchange"/> directly — a cross-origin POST with credentials — to redeem the
/// code and get the DXSuite cookie set, then does a single plain top-level navigation straight to
/// the target DXSuite page once that succeeds. There is no separate callback page to visit: the
/// browser's one and only navigation is the real destination, already authenticated.
/// </summary>
[Route("bridge")]
[EnableCors(CorsPolicy)]
public class BridgeAuthController(
    EditorBridgeClient bridgeClient,
    ITenantStore tenantStore,
    IOptions<EditorBridgeClientOptions> options,
    IConfiguration configuration,
    ILogger<BridgeAuthController> logger) : Controller
{
    public const string CorsPolicy = "EditorBridgeUi";
    private readonly EditorBridgeClientOptions _options = options.Value;

    /// <summary>
    /// Only a plain in-app path is ever honored: anything else (an absolute URL, a scheme, a
    /// protocol-relative "//host/..." path) could turn this into an open redirect, since the value
    /// arrives from the Director-controlled caller, not from DXSuite itself.
    /// </summary>
    private static string SafeLocalPath(string? returnTo) =>
        !string.IsNullOrWhiteSpace(returnTo) && returnTo.StartsWith('/') && !returnTo.StartsWith("//", StringComparison.Ordinal)
            && !returnTo.Contains('\\') && !returnTo.Contains("://", StringComparison.Ordinal)
            ? returnTo : "/Flight-Data-Editor";

    public sealed record ExchangeRequest(string Code, string Verifier, string? ReturnTo);

    [HttpPost("dxsuite-exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest body, CancellationToken ct)
    {
        logger.LogInformation("[BRIDGE] 1/5 exchange called. Host={Host} PathBase={PathBase} Origin={Origin} Enabled={Enabled} DirectorBaseUrl='{Url}' ClientId='{ClientId}' HasSecret={HasSecret}",
            Request.Host, Request.PathBase, Request.Headers.Origin.ToString(), _options.Enabled,
            _options.DirectorBaseUrl, _options.ClientId, !string.IsNullOrEmpty(_options.ClientSecret));

        BridgeFileLog.Write($"[BRIDGE] exchange called Host={Request.Host} PathBase={Request.PathBase} Origin={Request.Headers.Origin} Enabled={_options.Enabled} DirectorBaseUrl='{_options.DirectorBaseUrl}' ClientId='{_options.ClientId}' HasSecret={!string.IsNullOrEmpty(_options.ClientSecret)} ReturnTo='{body?.ReturnTo}'");
        if (!_options.Enabled)
        {
            BridgeFileLog.Write("[BRIDGE] FAIL: EditorBridgeClient:Enabled is false -> 404");
            logger.LogWarning("[BRIDGE] FAIL at 1/5: EditorBridgeClient:Enabled is false -> 404.");
            return NotFound();
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Verifier))
        {
            BridgeFileLog.Write("[BRIDGE] FAIL: body missing code or verifier -> 400");
            logger.LogWarning("[BRIDGE] FAIL at 2/5: request body missing code or verifier (bodyNull={BodyNull}).", body is null);
            return BadRequest(new { error = "missing-code-or-verifier" });
        }

        var identity = await bridgeClient.RedeemAsync(body.Code, body.Verifier, ct);
        if (identity is null)
        {
            BridgeFileLog.Write("[BRIDGE] FAIL: Director redeem returned no identity -> 401 (check [BRIDGE-HTTP] line for status; usually ClientSecret mismatch, bad/expired code or PKCE verifier)");
            logger.LogWarning("[BRIDGE] FAIL at 3/5: Director redeem returned no identity (see preceding [BRIDGE-HTTP] line for status/reason).");
            return Unauthorized(new { error = "exchange-failed" });
        }
        BridgeFileLog.Write($"[BRIDGE] redeem OK User={identity.UserName} TenantId={identity.TenantId}");
        logger.LogInformation("[BRIDGE] 3/5 redeem OK. User={User} TenantId={TenantId}", identity.UserName, identity.TenantId);

        var returnTo = SafeLocalPath(body.ReturnTo);

        var tenant = await tenantStore.FindByTenantIdAsync(identity.TenantId, ct);
        var needsFlightDataEditorFlag = returnTo.StartsWith("/Flight-Data-Editor", StringComparison.OrdinalIgnoreCase);
        if (tenant is null || !tenant.IsEnabled || !tenant.Modules.Aviation || (needsFlightDataEditorFlag && !tenant.Features.FlightDataEditor))
        {
            var bySubdomain = await tenantStore.FindBySubdomainAsync("Default", ct);
            logger.LogWarning("Diag: FindBySubdomainAsync(\"Default\") -> found={Found2}, aviation={Aviation2}, tenantId={TenantId2}",
                bySubdomain is not null, bySubdomain?.Modules.Aviation, bySubdomain?.TenantId);
            var modulesSection = configuration.GetSection("Tenants:Default:Modules");
            logger.LogWarning("Diag: raw config -> sectionExists={Exists}, rawAviation={RawAviation}, keys=[{Keys}]",
                modulesSection.Exists(), configuration["Tenants:Default:Modules:Aviation"],
                string.Join(",", modulesSection.GetChildren().Select(c => $"{c.Key}={c.Value}")));
            logger.LogWarning("Editor bridge identity for tenant '{TenantId}' has no matching enabled DXSuite tenant for '{ReturnTo}' " +
                "(found={Found}, enabled={Enabled}, aviation={Aviation}, flightDataEditor={Fde}, " +
                "director={Director}, coreData={CoreData}, storeType={StoreType}, recordTenantId={RecordTenantId}, recordSubdomain={RecordSubdomain}).",
                identity.TenantId, returnTo, tenant is not null, tenant?.IsEnabled, tenant?.Modules.Aviation, tenant?.Features.FlightDataEditor,
                tenant?.Modules.Director, tenant?.Modules.CoreData, tenantStore.GetType().FullName, tenant?.TenantId, tenant?.Subdomain);
            BridgeFileLog.Write($"[BRIDGE] FAIL: tenant '{identity.TenantId}' not available for '{returnTo}' (found={tenant is not null}, enabled={tenant?.IsEnabled}, aviation={tenant?.Modules.Aviation}, flightDataEditor={tenant?.Features.FlightDataEditor}) -> 403");
            return StatusCode((int)HttpStatusCode.Forbidden, new { error = "tenant-not-available" });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, identity.UserName),
            new("preferred_username", identity.UserName),
            new("name", identity.DisplayName),
            new(BridgeSessionClaimTypes.IsBridgeSession, "1"),
            new(BridgeSessionClaimTypes.CustomerId, identity.CustomerId),
            new(BridgeSessionClaimTypes.TenantId, identity.TenantId),
            new(BridgeSessionClaimTypes.RoleName, identity.RoleName),
            new(BridgeSessionClaimTypes.Permissions, string.Join(",", identity.Permissions)),
            new(BridgeSessionClaimTypes.Operations, string.Join(",", identity.Operations)),
            new(BridgeSessionClaimTypes.SessionHandle, identity.SessionHandle),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = identity.ExpiresAt });

        // Read by TenantResolutionMiddleware on the no-subdomain requests that follow (e.g. localhost
        // dev) so this bridge-authenticated user lands in their own tenant, not the host's default.
        Response.Cookies.Append(TenantResolutionMiddleware.BridgeTenantCookie, identity.TenantId, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_options.AllowLoopbackHttp,
            SameSite = SameSiteMode.Lax,
            Expires = identity.ExpiresAt,
        });

        BridgeFileLog.Write($"[BRIDGE] SUCCESS cookie issued for {identity.UserName}; redirectTo={returnTo}");
        logger.LogInformation("[BRIDGE] 5/5 cookie issued for {User}; redirectTo={ReturnTo}", identity.UserName, returnTo);
        return Ok(new { redirectTo = returnTo });
    }
}
