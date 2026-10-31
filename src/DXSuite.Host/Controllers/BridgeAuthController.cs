using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DXSuite.Host.Bridge;
using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Models;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Controllers;

/// <summary>
/// Receives the Editor bridge handoff from Director: Director's Angular app already holds a
/// Director-user JWT, calls Director's /api/v1/editor-auth/launch with a PKCE challenge, and does a
/// full browser navigation here with the resulting one-time code. The PKCE verifier travels in the
/// URL fragment (never sent to this or any server by the browser) rather than the query string, so
/// <see cref="Callback"/> serves a tiny relay page that reads the fragment client-side and posts
/// both to <see cref="Exchange"/>, which does the actual server-to-server redeem call to Director.
/// </summary>
[Route("bridge")]
public class BridgeAuthController(
    EditorBridgeClient bridgeClient,
    ITenantStore tenantStore,
    IOptions<EditorBridgeClientOptions> options,
    ILogger<BridgeAuthController> logger) : Controller
{
    private readonly EditorBridgeClientOptions _options = options.Value;

    [HttpGet("dxsuite-callback")]
    public IActionResult Callback([FromQuery] string? code, [FromQuery] string? returnTo)
    {
        if (!_options.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(code)) return Content(RelayHtml.Error("Missing authorization code."), "text/html");
        return Content(RelayHtml.Relay(code, SafeLocalPath(returnTo)), "text/html");
    }

    /// <summary>
    /// Only a plain in-app path is ever honored: anything else (an absolute URL, a scheme, a
    /// protocol-relative "//host/..." path) could turn this into an open redirect, since the value
    /// arrives from the Director-controlled link/query string, not from DXSuite itself.
    /// </summary>
    private static string SafeLocalPath(string? returnTo) =>
        !string.IsNullOrWhiteSpace(returnTo) && returnTo.StartsWith('/') && !returnTo.StartsWith("//", StringComparison.Ordinal)
            && !returnTo.Contains('\\') && !returnTo.Contains("://", StringComparison.Ordinal)
            ? returnTo : "/Flight-Data-Editor";

    public sealed record ExchangeRequest(string Code, string Verifier, string? ReturnTo);

    [HttpPost("dxsuite-exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest body, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Verifier))
            return BadRequest(new { error = "missing-code-or-verifier" });

        var identity = await bridgeClient.RedeemAsync(body.Code, body.Verifier, ct);
        if (identity is null) return Unauthorized(new { error = "exchange-failed" });

        var returnTo = SafeLocalPath(body.ReturnTo);

        var tenant = await tenantStore.FindByTenantIdAsync(identity.TenantId, ct);
        var needsFlightDataEditorFlag = returnTo.StartsWith("/Flight-Data-Editor", StringComparison.OrdinalIgnoreCase);
        if (tenant is null || !tenant.IsEnabled || !tenant.Modules.Aviation || (needsFlightDataEditorFlag && !tenant.Features.FlightDataEditor))
        {
            logger.LogWarning("Editor bridge identity for tenant '{TenantId}' has no matching enabled DXSuite tenant for '{ReturnTo}'.", identity.TenantId, returnTo);
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

        return Ok(new { redirectTo = returnTo });
    }

    private static class RelayHtml
    {
        private const string Template = """
            <!DOCTYPE html>
            <html><head><title>Signing in…</title></head>
            <body data-code="__CODE__" data-return-to="__RETURN_TO__">
            <p>Signing you in&hellip;</p>
            <script>
            (function () {
                var code = document.body.getAttribute('data-code');
                var returnTo = document.body.getAttribute('data-return-to');
                var hash = window.location.hash.length > 1 ? window.location.hash.slice(1) : '';
                var verifier = new URLSearchParams(hash).get('verifier') || '';
                fetch('/bridge/dxsuite-exchange', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'same-origin',
                    body: JSON.stringify({ code: code, verifier: verifier, returnTo: returnTo })
                }).then(function (r) {
                    if (!r.ok) throw new Error('exchange-failed');
                    return r.json();
                }).then(function (data) {
                    window.location.replace(data.redirectTo || '/Flight-Data-Editor');
                }).catch(function () {
                    document.body.textContent = 'Sign-in failed. Please return to Director and try again.';
                });
            })();
            </script>
            </body></html>
            """;

        public static string Relay(string code, string returnTo) => Template
            .Replace("__CODE__", WebUtility.HtmlEncode(code))
            .Replace("__RETURN_TO__", WebUtility.HtmlEncode(returnTo));

        public static string Error(string message) =>
            $"<!DOCTYPE html><html><body>{WebUtility.HtmlEncode(message)}</body></html>";
    }
}
