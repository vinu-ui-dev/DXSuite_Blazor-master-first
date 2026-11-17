using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Cors;
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
    IDataProtectionProvider dataProtection,
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

    /// <summary>
    /// Shared by <see cref="Exchange"/> (Angular-driven) and <see cref="Callback"/> (DXSuite-driven):
    /// checks the redeemed identity's tenant, then issues the DXSuite auth cookie and tenant cookie.
    /// Returns null on success, or the failure result to send back.
    /// </summary>
    private async Task<IActionResult?> CompleteSignInAsync(BridgeIdentity identity, string returnTo, CancellationToken ct)
    {
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
        return null;
    }

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

        var failure = await CompleteSignInAsync(identity, returnTo, ct);
        if (failure is not null) return failure;

        return Ok(new { redirectTo = returnTo });
    }

    // ── DXSuite-initiated flow (what Director's Angular /editor-launch page implements) ──────────
    // 1. A signed-out user opens a DXSuite editor page; Start makes the PKCE verifier, challenge and
    //    state, keeps them in a short-lived protected cookie, and sends the browser to Director's
    //    /editor-launch page with the challenge.
    // 2. Director (already signed in) obtains the code and redirects back to Callback.
    // 3. Callback checks state, redeems the code with the stored verifier and signs the user in.

    public const string PkceCookie = "dx-bridge-pkce";
    private sealed record PkceState(string State, string Verifier, string ReturnTo);

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string CallbackUrl() =>
        !string.IsNullOrWhiteSpace(_options.RedirectUri)
            ? _options.RedirectUri
            : $"{Request.Scheme}://{Request.Host}{Request.PathBase}/bridge/dxsuite-callback";

    private string? DirectorUiBase() =>
        (!string.IsNullOrWhiteSpace(_options.DirectorUiBaseUrl) ? _options.DirectorUiBaseUrl : _options.AllowedUiOrigins.FirstOrDefault())?.TrimEnd('/');

    private CookieOptions PkceCookieOptions(TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        Secure = !_options.AllowLoopbackHttp,
        SameSite = SameSiteMode.Lax,
        Path = string.IsNullOrEmpty(Request.PathBase.Value) ? "/" : Request.PathBase.Value,
        MaxAge = lifetime,
    };

    [HttpGet("start")]
    public IActionResult Start([FromQuery] string? returnTo)
    {
        var ui = DirectorUiBase();
        if (!_options.Enabled || string.IsNullOrEmpty(ui))
        {
            BridgeFileLog.Write($"[BRIDGE] start FAIL: Enabled={_options.Enabled}, DirectorUiBaseUrl/AllowedUiOrigins empty={string.IsNullOrEmpty(ui)}");
            return NotFound();
        }

        var verifier = RandomToken();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = RandomToken();
        var safeReturn = SafeLocalPath(returnTo);

        var protector = dataProtection.CreateProtector("DXSuite.EditorBridge.Pkce");
        Response.Cookies.Append(PkceCookie,
            protector.Protect(JsonSerializer.Serialize(new PkceState(state, verifier, safeReturn))),
            PkceCookieOptions(TimeSpan.FromMinutes(10)));

        // Director's SPA is served from this same origin (https://site/ and https://site/director): its login token
        // and customer are readable here, so request the code directly. This avoids needing the SPA to serve a
        // deep link such as /editor-launch (a 404 unless the host rewrites unknown paths to index.html).
        if (Uri.TryCreate(ui, UriKind.Absolute, out var uiUri) &&
            string.Equals(uiUri.GetLeftPart(UriPartial.Authority), $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase))
        {
            BridgeFileLog.Write($"[BRIDGE] start (same-origin relay) returnTo={safeReturn}");
            return Content(RelayPage(ui, _options.DirectorBaseUrl.TrimEnd('/'), CallbackUrl(), challenge, state), "text/html");
        }

        var target = $"{ui}/editor-launch" +
            $"?clientId={Uri.EscapeDataString(_options.ClientId)}" +
            $"&redirectUri={Uri.EscapeDataString(CallbackUrl())}" +
            $"&codeChallenge={Uri.EscapeDataString(challenge)}" +
            $"&state={Uri.EscapeDataString(state)}";
        BridgeFileLog.Write($"[BRIDGE] start -> {ui}/editor-launch (redirectUri={CallbackUrl()}, returnTo={safeReturn})");
        return Redirect(target);
    }

    private string RelayPage(string ui, string apiBase, string callback, string challenge, string state)
    {
        static string J(string v) => JsonSerializer.Serialize(v);
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>Signing in…</title></head>
            <body><p id="m">Signing you in&hellip;</p>
            <script>
            (async function () {
                var ui = {{J(ui)}}, api = {{J(apiBase)}}, callback = {{J(callback)}};
                var token = localStorage.getItem('token'), user = null;
                try { user = JSON.parse(localStorage.getItem('userList') || 'null'); } catch (e) { }
                var customerId = user && user.customerId;
                if (!token || !customerId) { location.replace(ui + '/'); return; }
                var r = await fetch(api + '/api/v1/editor-auth/launch', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token },
                    body: JSON.stringify({ clientId: {{J(_options.ClientId)}}, customerId: customerId, redirectUri: callback, codeChallenge: {{J(challenge)}} })
                });
                if (!r.ok) { var t = ''; try { t = (await r.text()).slice(0, 300); } catch (e) { } throw new Error('launch returned HTTP ' + r.status + (t ? ': ' + t : '')); }
                var d = await r.json();
                location.replace(callback + '?code=' + encodeURIComponent(d.code) + '&state=' + encodeURIComponent({{J(state)}}));
            })().catch(function (e) {
                var msg = (e && e.message) ? e.message : String(e);
                document.getElementById('m').textContent = 'Editor sign-in failed. Return to Director and try again. (' + msg + ')';
                try { fetch(callback.replace('dxsuite-callback', 'relay-log'), { method: 'POST', headers: { 'Content-Type': 'text/plain' }, body: msg }); } catch (x) { }
            });
            </script></body></html>
            """;
    }

    [HttpPost("relay-log")]
    [Consumes("text/plain")]
    public async Task<IActionResult> RelayLog()
    {
        using var reader = new StreamReader(Request.Body);
        var text = (await reader.ReadToEndAsync()).ReplaceLineEndings(" ");
        BridgeFileLog.Write($"[BRIDGE] relay FAIL (browser): {(text.Length > 400 ? text[..400] : text)}");
        return NoContent();
    }

    [HttpGet("dxsuite-callback")]
    // Same handler under the callback URL already registered with Director for the "director" client.
    [HttpGet("~/auth/dxsuite/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();

        PkceState? saved = null;
        if (Request.Cookies.TryGetValue(PkceCookie, out var protectedValue))
        {
            try
            {
                saved = JsonSerializer.Deserialize<PkceState>(dataProtection.CreateProtector("DXSuite.EditorBridge.Pkce").Unprotect(protectedValue));
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                BridgeFileLog.Write($"[BRIDGE] callback FAIL: PKCE cookie unreadable ({ex.GetType().Name})");
            }
        }
        Response.Cookies.Delete(PkceCookie, PkceCookieOptions(null));

        if (saved is null || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(saved.State), Encoding.UTF8.GetBytes(state)))
        {
            BridgeFileLog.Write($"[BRIDGE] callback FAIL: missing/expired PKCE cookie or state mismatch (cookie={(saved is not null)}, code={!string.IsNullOrWhiteSpace(code)})");
            return Redirect($"{Request.PathBase}/?error=editor-sign-in-failed");
        }

        var identity = await bridgeClient.RedeemAsync(code, saved.Verifier, ct);
        if (identity is null)
        {
            BridgeFileLog.Write("[BRIDGE] callback FAIL: Director redeem returned no identity (see [BRIDGE-HTTP] line: ClientSecret mismatch, expired code, or RedirectUri mismatch)");
            return Redirect($"{Request.PathBase}/?error=editor-sign-in-failed");
        }

        var failure = await CompleteSignInAsync(identity, saved.ReturnTo, ct);
        if (failure is not null) return Redirect($"{Request.PathBase}/?error=editor-tenant-unavailable");

        BridgeFileLog.Write($"[BRIDGE] callback SUCCESS for {identity.UserName}; redirectTo={saved.ReturnTo}");
        return Redirect($"{Request.PathBase}{saved.ReturnTo}");
    }
}
