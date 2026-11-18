using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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
/// DXSuite side of the Director -> DXSuite Editor bridge (SSO). It is DXSuite-initiated, matching
/// Director's Angular /editor-launch page:
/// <list type="number">
/// <item><see cref="Start"/>: a signed-out user opens a DXSuite editor page (the link Director shows).
/// DXSuite creates a PKCE verifier, challenge and state, keeps them in a short-lived protected cookie,
/// and sends the browser to Director's /editor-launch page with the challenge.</item>
/// <item>Director (already signed in) obtains the one-time code and redirects the browser to
/// <see cref="Callback"/> with <c>code</c> and <c>state</c>.</item>
/// <item><see cref="Callback"/> checks the state, redeems the code server-to-server with the stored
/// verifier and signs the user in, then redirects to the page originally requested.</item>
/// </list>
/// <see cref="Exchange"/> is the older same-origin variant where the caller already holds the verifier.
/// </summary>
[Route("bridge")]
public class BridgeAuthController(
    EditorBridgeClient bridgeClient,
    ITenantStore tenantStore,
    IOptions<EditorBridgeClientOptions> options,
    IDataProtectionProvider dataProtection,
    ILogger<BridgeAuthController> logger) : Controller
{
    public const string PkceCookie = "dx-bridge-pkce";
    private const string DefaultReturnTo = "/Flight-Data-Editor";

    private readonly EditorBridgeClientOptions _options = options.Value;

    private sealed record PkceState(string State, string Verifier, string ReturnTo);

    /// <summary>
    /// Only a plain in-app path is ever honored: anything else (an absolute URL, a scheme, a
    /// protocol-relative "//host/..." path) could turn this into an open redirect, since the value
    /// arrives from a query string / request body, not from DXSuite itself.
    /// </summary>
    internal static string SafeLocalPath(string? returnTo) =>
        !string.IsNullOrWhiteSpace(returnTo) && returnTo.StartsWith('/') && !returnTo.StartsWith("//", StringComparison.Ordinal)
            && !returnTo.Contains('\\') && !returnTo.Contains("://", StringComparison.Ordinal)
            && !returnTo.Any(char.IsControl)
            ? returnTo : DefaultReturnTo;

    /// <summary>One line per step in logs/bridge-yyyyMMdd.log (no codes, verifiers, states or secrets).</summary>
    private void Trace(string step) =>
        BridgeFileLog.Write($"[BRIDGE] {step} | host={Request.Host} scheme={Request.Scheme} pathBase='{Request.PathBase}' path={Request.Path}");

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool PathIs(string returnTo, string path)
    {
        var onlyPath = returnTo.Split('?', '#')[0];
        return onlyPath.Equals(path, StringComparison.OrdinalIgnoreCase)
            || onlyPath.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the identity's tenant has what the requested page needs.</summary>
    private static bool TenantAllows(TenantRecord? tenant, string returnTo)
    {
        if (tenant is null || !tenant.IsEnabled) return false;
        if (PathIs(returnTo, "/messages")) return tenant.Modules.Director && tenant.Features.Messaging;
        if (!tenant.Modules.Aviation) return false;
        if (PathIs(returnTo, "/Flight-Data-Editor")) return tenant.Features.FlightDataEditor;
        return true; // CUPPS and other Aviation pages
    }

    /// <summary>
    /// Checks the redeemed identity's tenant, then issues the DXSuite auth cookie and tenant cookie.
    /// Returns false (and logs why) when the tenant cannot serve <paramref name="returnTo"/>.
    /// </summary>
    private async Task<bool> CompleteSignInAsync(BridgeIdentity identity, string returnTo, CancellationToken ct)
    {
        // Director and DXSuite may name the same tenant differently; an explicit map reconciles them.
        if (_options.TenantIdMap.TryGetValue(identity.TenantId, out var mappedTenantId) && !string.IsNullOrWhiteSpace(mappedTenantId))
        {
            Trace($"tenant mapped via EditorBridgeClient:TenantIdMap: '{identity.TenantId}' -> '{mappedTenantId}'");
            identity = identity with { TenantId = mappedTenantId };
        }

        var tenant = await tenantStore.FindByTenantIdAsync(identity.TenantId, ct);
        if (!TenantAllows(tenant, returnTo))
        {
            var knownTenants = string.Join(", ", (await tenantStore.GetAllEnabledAsync(ct)).Select(t => $"'{t.TenantId}' (subdomain '{t.Subdomain}')"));
            Trace($"FAIL tenant: Director sent TenantId='{identity.TenantId}' but DXSuite's enabled tenants are [{knownTenants}] - EditorBridge:Customers:<customerId>:TenantId on the Director side must equal one of those TenantId values. identity.TenantId='{identity.TenantId}' cannot serve '{returnTo}' (tenantFound={tenant is not null}, enabled={tenant?.IsEnabled}, aviation={tenant?.Modules.Aviation}, director={tenant?.Modules.Director}, flightDataEditor={tenant?.Features.FlightDataEditor}, messaging={tenant?.Features.Messaging}) -> redirect editor-tenant-unavailable");
            logger.LogWarning(
                "[BRIDGE] tenant '{TenantId}' cannot serve '{ReturnTo}' (found={Found}, enabled={Enabled}, aviation={Aviation}, director={Director}, flightDataEditor={Fde}, messaging={Messaging}).",
                identity.TenantId, returnTo, tenant is not null, tenant?.IsEnabled, tenant?.Modules.Aviation,
                tenant?.Modules.Director, tenant?.Features.FlightDataEditor, tenant?.Features.Messaging);
            LoginAuditLog.LoginFailed("bridge", identity.UserName, "tenant-unavailable", HttpContext,
                $"role='{identity.RoleName}' tenant='{identity.TenantId}' returnTo='{returnTo}' tenantFound={tenant is not null} enabled={tenant?.IsEnabled}");
            return false;
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

        LoginAuditLog.LoginSucceeded("bridge", identity.UserName, HttpContext, identity.RoleName, identity.TenantId,
            identity.CustomerId, identity.Permissions, identity.Operations,
            $"displayName='{identity.DisplayName}' redirectTo='{returnTo}' expires={identity.ExpiresAt:u}");
        logger.LogInformation("[BRIDGE] cookie issued for {User}; redirectTo={ReturnTo}", identity.UserName, returnTo);
        Trace($"OK sign-in cookie + tenant cookie issued: user='{identity.UserName}' role='{identity.RoleName}' tenant='{identity.TenantId}' customer='{identity.CustomerId}' permissions=[{string.Join(',', identity.Permissions)}] cookieExpires={identity.ExpiresAt:u} -> redirect {returnTo}");
        return true;
    }

    // ── DXSuite-initiated flow ────────────────────────────────────────────────────────────────

    private string CallbackUrl() =>
        !string.IsNullOrWhiteSpace(_options.RedirectUri)
            ? _options.RedirectUri
            : $"{Request.Scheme}://{Request.Host}{Request.PathBase}/auth/dxsuite/callback";

    private CookieOptions PkceCookieOptions(TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        Secure = !_options.AllowLoopbackHttp,
        // Lax: the cookie must come back on the top-level GET redirect from Director to the callback.
        SameSite = SameSiteMode.Lax,
        Path = string.IsNullOrEmpty(Request.PathBase.Value) ? "/" : Request.PathBase.Value,
        MaxAge = lifetime,
    };

    [HttpGet("start")]
    public IActionResult Start([FromQuery] string? returnTo)
    {
        var ui = _options.DirectorUiBaseUrl?.TrimEnd('/');
        Trace($"1 start: returnTo='{returnTo}' enabled={_options.Enabled} clientId='{_options.ClientId}' directorUi='{ui}' directorApi='{_options.DirectorBaseUrl}' redirectUri='{_options.RedirectUri}' hasSecret={!string.IsNullOrEmpty(_options.ClientSecret)}");
        if (!_options.Enabled || string.IsNullOrEmpty(ui))
        {
            Trace("FAIL start: EditorBridgeClient not enabled or DirectorUiBaseUrl empty -> 404");
            logger.LogWarning("[BRIDGE] start refused: Enabled={Enabled}, DirectorUiBaseUrl empty={UiEmpty}.", _options.Enabled, string.IsNullOrEmpty(ui));
            return NotFound();
        }

        var verifier = RandomToken();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = RandomToken();
        var safeReturn = SafeLocalPath(returnTo);

        // Director's own /editor-launch page does the same launch call (no CustomerId, picker when needed).
        // Director's Angular app uses hash routing (https://site/#/home), so the route lives after the "#".
        var launchUrl = $"{ui}/#/editor-launch" +
            $"?clientId={Uri.EscapeDataString(_options.ClientId)}" +
            $"&redirectUri={Uri.EscapeDataString(CallbackUrl())}" +
            $"&codeChallenge={Uri.EscapeDataString(challenge)}" +
            $"&state={Uri.EscapeDataString(state)}";

        Response.Cookies.Append(PkceCookie,
            dataProtection.CreateProtector("DXSuite.EditorBridge.Pkce")
                .Protect(JsonSerializer.Serialize(new PkceState(state, verifier, safeReturn))),
            PkceCookieOptions(TimeSpan.FromMinutes(10)));

        // Director's SPA served from this same origin (https://site/ and https://site/director): its login
        // token and customer are readable here, so request the code directly. That avoids needing the SPA to
        // answer a deep link such as /editor-launch (a 404 unless the host rewrites unknown paths).
        if (Uri.TryCreate(ui, UriKind.Absolute, out var uiUri) &&
            string.Equals(uiUri.GetLeftPart(UriPartial.Authority), $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase))
        {
            Trace($"2 start: same-origin relay page sent; callback='{CallbackUrl()}', PKCE cookie set (Secure={!_options.AllowLoopbackHttp}, Path='{PkceCookieOptions(null).Path}')");
            logger.LogInformation("[BRIDGE] start (same-origin relay) clientId={ClientId}, redirectUri={RedirectUri}, returnTo={ReturnTo}", _options.ClientId, CallbackUrl(), safeReturn);
            return Content(RelayPage(ui, _options.DirectorBaseUrl.TrimEnd('/'), CallbackUrl(), challenge, state, launchUrl), "text/html");
        }

        Trace($"2 start: redirecting browser to Director UI '{ui}/#/editor-launch'; callback='{CallbackUrl()}'");
        logger.LogInformation("[BRIDGE] start -> {Ui}/editor-launch (clientId={ClientId}, redirectUri={RedirectUri}, returnTo={ReturnTo})", ui, _options.ClientId, CallbackUrl(), safeReturn);
        return Redirect(launchUrl);
    }

    private string RelayPage(string ui, string apiBase, string callback, string challenge, string state, string launchUrl)
    {
        static string J(string v) => JsonSerializer.Serialize(v).Replace("<", "\\u003c");
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>Signing in…</title></head>
            <body><p id="m">Signing you in&hellip;</p>
            <script>
            (async function () {
                var ui = {{J(ui)}}, api = {{J(apiBase)}}, callback = {{J(callback)}}, launchUrl = {{J(launchUrl)}};
                var token = localStorage.getItem('token');
                if (!token) { location.replace(ui + '/'); return; }
                var r = await fetch(api + '/api/v1/editor-auth/launch', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token },
                    // No customerId on purpose: Director picks it from the signed-in user's own customers.
                    body: JSON.stringify({ clientId: {{J(_options.ClientId)}}, redirectUri: callback, codeChallenge: {{J(challenge)}} })
                });
                if (!r.ok) throw new Error('launch returned HTTP ' + r.status);
                var d = await r.json();
                // More than one customer qualifies: let Director's /editor-launch page show the picker.
                if (!d.code) { location.replace(launchUrl); return; }
                location.replace(callback + '?code=' + encodeURIComponent(d.code) + '&state=' + encodeURIComponent({{J(state)}}));
            })().catch(function (e) {
                document.getElementById('m').textContent = 'Editor sign-in failed. Return to Director and try again. (' + ((e && e.message) ? e.message : String(e)) + ')';
            });
            </script></body></html>
            """;
    }

    // The callback URL registered with Director (EditorBridge:Clients:<id>:RedirectUri) and sent as redirectUri:
    // https://<site>/director/auth/dxsuite/callback  (PathBase "/director" + "/auth/dxsuite/callback").
    // "bridge/dxsuite-callback" is kept as a plain alias.
    [HttpGet("~/auth/dxsuite/callback")]
    [HttpGet("dxsuite-callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        Trace($"3 callback reached: codePresent={!string.IsNullOrWhiteSpace(code)} statePresent={!string.IsNullOrWhiteSpace(state)} requestCookies=[{string.Join(',', Request.Cookies.Keys)}] forwardedProto='{Request.Headers["X-Forwarded-Proto"]}'");
        if (!_options.Enabled)
        {
            Trace("FAIL callback: EditorBridgeClient:Enabled is false -> 404");
            return NotFound();
        }

        PkceState? saved = null;
        var cookiePresent = Request.Cookies.TryGetValue(PkceCookie, out var protectedValue);
        if (cookiePresent)
        {
            try
            {
                saved = JsonSerializer.Deserialize<PkceState>(
                    dataProtection.CreateProtector("DXSuite.EditorBridge.Pkce").Unprotect(protectedValue));
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                logger.LogWarning("[BRIDGE] callback: PKCE cookie unreadable ({Type}).", ex.GetType().Name);
                Trace($"FAIL callback: PKCE cookie present but unreadable ({ex.GetType().Name}) - Data Protection keys differ between start and callback (other instance, or keys not persisted across app pool recycle)");
            }
        }
        // One-shot: never let the same verifier/state be replayed.
        Response.Cookies.Delete(PkceCookie, PkceCookieOptions(null));

        if (saved is null || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(saved.State), Encoding.UTF8.GetBytes(state)))
        {
            logger.LogWarning("[BRIDGE] callback refused: PKCE cookie present={Cookie}, code present={Code}, state matches={State}.",
                saved is not null, !string.IsNullOrWhiteSpace(code),
                saved is not null && !string.IsNullOrWhiteSpace(state) &&
                    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(saved.State), Encoding.UTF8.GetBytes(state)));
            Trace($"FAIL callback: cookiePresent={cookiePresent} cookieReadable={saved is not null} codePresent={!string.IsNullOrWhiteSpace(code)} statePresent={!string.IsNullOrWhiteSpace(state)} -> redirect editor-sign-in-failed (no PKCE cookie = not sent back / expired / other path; state mismatch = another start request overwrote it)");
            var reason = saved is null ? (cookiePresent ? "cookie-unreadable" : "no-cookie")
                : string.IsNullOrWhiteSpace(code) ? "no-code"
                : string.IsNullOrWhiteSpace(state) ? "no-state" : "state-mismatch";
            LoginAuditLog.LoginFailed("bridge", null, $"callback-{reason}", HttpContext);
            return Redirect($"{Request.PathBase}/?error=editor-sign-in-failed&reason={reason}");
        }
        Trace("4 callback: PKCE cookie valid and state matches; calling Director /redeem");

        var identity = await bridgeClient.RedeemAsync(code, saved.Verifier, ct);
        if (identity is null)
        {
            // "http-403" here means Director itself refused the redeem (e.g. an Airline Agent role it will not hand an editor session to).
            LoginAuditLog.LoginFailed("bridge", null, $"director-redeem-{bridgeClient.LastFailure ?? "failed"}", HttpContext);
            Trace("FAIL callback: Director /redeem returned no identity -> redirect editor-sign-in-failed (see the [BRIDGE-HTTP] lines just above for the HTTP status)");
            logger.LogWarning("[BRIDGE] callback: Director redeem returned no identity (ClientSecret mismatch, expired code, or RedirectUri mismatch).");
            return Redirect($"{Request.PathBase}/?error=editor-sign-in-failed&reason=redeem-{bridgeClient.LastFailure ?? "failed"}");
        }

        Trace($"5 callback: redeem OK user='{identity.UserName}' tenant='{identity.TenantId}' role='{identity.RoleName}'");
        var returnTo = SafeLocalPath(saved.ReturnTo);
        if (!await CompleteSignInAsync(identity, returnTo, ct))
            return Redirect($"{Request.PathBase}/?error=editor-tenant-unavailable");

        return Redirect($"{Request.PathBase}{returnTo}");
    }

    // ── Same-origin variant: the caller already holds the verifier ───────────────────────────

    public sealed record ExchangeRequest(string Code, string Verifier, string? ReturnTo);

    [HttpPost("dxsuite-exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest body, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        if (body is null || string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Verifier))
            return BadRequest(new { error = "missing-code-or-verifier" });

        var identity = await bridgeClient.RedeemAsync(body.Code, body.Verifier, ct);
        if (identity is null)
        {
            LoginAuditLog.LoginFailed("bridge-exchange", null, $"director-redeem-{bridgeClient.LastFailure ?? "failed"}", HttpContext);
            return Unauthorized(new { error = "exchange-failed" });
        }

        var returnTo = SafeLocalPath(body.ReturnTo);
        if (!await CompleteSignInAsync(identity, returnTo, ct))
            return StatusCode((int)HttpStatusCode.Forbidden, new { error = "tenant-not-available" });

        return Ok(new { redirectTo = returnTo });
    }
}
