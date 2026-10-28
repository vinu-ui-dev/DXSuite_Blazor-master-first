using System;
using System.Linq;
using System.Threading.Tasks;
using DXSuite.Director.Services.LocationBundles;
using DXSuite.Director.Services.Panels;
using DXSuite.Shared.Models;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Serves a panel's files to an anonymous touch surface. A panel is a location bundle's
/// HTML/JS/CSS rendered outside the Blazor app: no login, no nav chrome, and not a location
/// page. The bundle storage and versioning pipeline is reused; the bundle *runtime*
/// (Location.razor's iframe and the cookie-authenticated Location Content API) is not.
///
/// The token is a path segment and stays in the URL permanently. The consumer is a player
/// webzone that performs a plain navigation to a URL configured once in a layout: there is no
/// operator to follow an enrollment link, and a CEF browser profile cannot be relied on to
/// keep a cookie across restarts. The usual objections to tokens in URLs — shoulder-surfing,
/// browser history, referrer leakage — do not apply to a URL typed once into a player config.
/// The residual exposure is that whoever can read the layout can assign content in that one
/// room, which is roughly whoever could walk up and touch the panel.
/// </summary>
[ApiController]
[AllowAnonymous]
public class PanelController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = BuildContentTypeProvider();

    private readonly IPanelService _panels;
    private readonly ILocationBundleService _bundleService;
    private readonly IMediaStore _mediaStore;
    private readonly ITenantContext _tenant;
    private readonly IOptions<FeatureFlags> _featureFlags;

    public PanelController(
        IPanelService panels,
        ILocationBundleService bundleService,
        IMediaStore mediaStore,
        ITenantContext tenant,
        IOptions<FeatureFlags> featureFlags)
    {
        _panels = panels;
        _bundleService = bundleService;
        _mediaStore = mediaStore;
        _tenant = tenant;
        _featureFlags = featureFlags;
    }

    /// <summary>
    /// Handles both /panel/{key}/{token} and its trailing-slash form, and serves the manifest's
    /// declared entry document for the latter.
    ///
    /// These cannot be two actions. Routing strips a trailing slash from the *template*, so
    /// "/panel/{panelKey}/{token}/" collapses onto "/panel/{panelKey}/{token}" and the two
    /// register as duplicate endpoints — every request to either form then fails with
    /// AmbiguousMatchException. Request.Path keeps the slash, so the branch belongs here.
    ///
    /// The redirect itself is load-bearing rather than cosmetic: served without the trailing
    /// slash, a relative "panel.css" in the entry document resolves to /panel/{key}/panel.css
    /// and silently drops the token. With it, relative URLs resolve correctly and the bundle
    /// never needs to know its own mount point.
    /// </summary>
    [HttpGet("/panel/{panelKey}/{token}")]
    public Task<IActionResult> GetRoot(string panelKey, string token)
    {
        if (!_featureFlags.Value.Panels) return Task.FromResult<IActionResult>(NotFound());

        var path = Request.Path.Value;
        if (path == null || !path.EndsWith('/'))
        {
            return Task.FromResult<IActionResult>(RedirectPermanent(
                $"/panel/{Uri.EscapeDataString(panelKey)}/{Uri.EscapeDataString(token)}/"));
        }

        return ServeAsync(panelKey, token, null);
    }

    [HttpGet("/panel/{panelKey}/{token}/{**path}")]
    public Task<IActionResult> GetFile(string panelKey, string token, string path) =>
        ServeAsync(panelKey, token, path);

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>A null path means "serve the manifest's entry document".</summary>
    private async Task<IActionResult> ServeAsync(string panelKey, string token, string? path)
    {
        if (!_featureFlags.Value.Panels) return NotFound();

        if (path != null && (path.Length == 0 || !IsSafeRelativePath(path)))
            return BadRequest();

        var panel = await _panels.ResolveAsync(panelKey, token, HttpContext.RequestAborted);

        // A bad key, a bad token, and a disabled panel are indistinguishable to the caller so
        // this route cannot be used to enumerate panel keys.
        if (panel == null) return NotFound();

        var resolved = await _bundleService.ResolveVersionAsync(
            panel.BundleKey, panel.PinnedVersion, HttpContext.RequestAborted);
        if (resolved == null) return NotFound();

        path ??= resolved.EntryPath;
        if (string.IsNullOrEmpty(path) || !IsSafeRelativePath(path)) return NotFound();

        var storagePath = LocationBundleConstants.StoragePath(panel.BundleKey, resolved.Version, path);
        var stream = await _mediaStore.OpenReadAsync(_tenant.TenantId, storagePath, HttpContext.RequestAborted);
        if (stream == null) return NotFound();

        var contentType = ResolveContentType(path);
        ApplyResponseHeaders(contentType);
        return File(stream, contentType);
    }

    private void ApplyResponseHeaders(string contentType)
    {
        if (IsHtml(contentType))
        {
            // The entry document must revalidate so a version flip reaches the panel on its
            // next reload; stale-if-error keeps the room alive through a Director outage.
            Response.Headers.CacheControl = "no-cache, stale-if-error=86400";

            // The global security middleware writes X-Frame-Options: DENY before MVC runs.
            // Remove it rather than setting SAMEORIGIN: a player webzone is not same-origin
            // with anything, so any value here can only break the render. WallZoneController
            // does the same for the same reason.
            Response.Headers.Remove("X-Frame-Options");
        }
        else
        {
            // Version-scoped paths are write-once (duplicate versions are rejected at import),
            // so static assets can be cached hard.
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }

    private static bool IsHtml(string contentType) =>
        contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    private static string ResolveContentType(string path) =>
        ContentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";

    private static bool IsSafeRelativePath(string path)
    {
        if (path.StartsWith('/') || path.Contains(':') || path.Contains('\\'))
            return false;
        return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private static FileExtensionContentTypeProvider BuildContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".mjs"] = "text/javascript";
        provider.Mappings.TryAdd(".wasm", "application/wasm");
        return provider;
    }
}
