using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using DXSuite.Director.Services.LocationBundles;
using DXSuite.Shared.Models;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Serves location bundle files to authenticated users. Bundles are never exposed
/// through the anonymous static-files middleware — every request re-checks that the
/// caller may access the location whose iframe is asking. locationId lives in the
/// path so relative asset URLs inside the bundle resolve under the same authorized
/// prefix without SDK involvement.
/// </summary>
[Route("api/location-bundles/content")]
[ApiController]
[Authorize]
public class LocationBundleContentController : ControllerBase
{
    /// <summary>Named HttpClient used for dev-override proxying.</summary>
    public const string DevProxyClientName = "LocationBundleDevProxy";

    private static readonly FileExtensionContentTypeProvider ContentTypes = BuildContentTypeProvider();

    private readonly ILocationAccessResolver _accessResolver;
    private readonly ILocationBundleService _bundleService;
    private readonly IDirectorDBContext _db;
    private readonly IMediaStore _mediaStore;
    private readonly ITenantContext _tenant;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LocationBundleOptions _options;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<LocationBundleContentController> _logger;

    public LocationBundleContentController(
        ILocationAccessResolver accessResolver,
        ILocationBundleService bundleService,
        IDirectorDBContext db,
        IMediaStore mediaStore,
        ITenantContext tenant,
        IHttpClientFactory httpClientFactory,
        IOptions<LocationBundleOptions> options,
        IOptions<FeatureFlags> featureFlags,
        ILogger<LocationBundleContentController> logger)
    {
        _accessResolver = accessResolver;
        _bundleService = bundleService;
        _db = db;
        _mediaStore = mediaStore;
        _tenant = tenant;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    [HttpGet("{locationId:int}/{version}/{**path}")]
    public async Task<IActionResult> GetFile(int locationId, string version, string path)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();

        if (string.IsNullOrEmpty(path) || !IsSafeRelativePath(path) || !IsSafeSegment(version))
            return BadRequest();

        var caller = await _accessResolver.ResolveAsync(User, locationId, HttpContext.RequestAborted);
        if (caller == null) return Unauthorized();
        if (!caller.IsAssigned) return Forbid();

        var location = await _db.TblLocations
            .AsNoTracking()
            .Include(l => l.LocationType)
            .FirstOrDefaultAsync(l => l.LocationId == locationId, HttpContext.RequestAborted);

        if (location == null || !LocationBundleConstants.IsBundleType(location.LocationType))
            return NotFound();

        var assignment = ExtendedDataHelper.Parse<LocationExtendedData>(location.ExtendedData)?.Bundle;
        if (assignment == null || string.IsNullOrEmpty(assignment.BundleKey))
            return NotFound();

        // Dev override serves the engineer's local files under the same authenticated
        // URL space. Triple-gated: assignment field + environment config + Admin caller.
        if (!string.IsNullOrEmpty(assignment.DevOverrideUrl) && _options.AllowDevOverride && caller.IsAdmin)
            return await ProxyDevOverrideAsync(assignment.DevOverrideUrl, path);

        var resolved = await _bundleService.ResolveVersionAsync(assignment.BundleKey, assignment.PinnedVersion, HttpContext.RequestAborted);
        if (resolved == null)
            return NotFound();

        // A stale tab still iframing an old version gets 409; the SDK reloads the page,
        // which re-resolves to the effective version.
        if (!string.Equals(resolved.Version, version, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "Bundle version has changed.", currentVersion = resolved.Version });

        var storagePath = LocationBundleConstants.StoragePath(assignment.BundleKey, resolved.Version, path);
        var stream = await _mediaStore.OpenReadAsync(_tenant.TenantId, storagePath, HttpContext.RequestAborted);
        if (stream == null)
            return NotFound();

        var contentType = ResolveContentType(path);
        ApplyResponseHeaders(contentType);
        return File(stream, contentType);
    }

    // ────────────────────────────────────────────────────────────────────────

    private async Task<IActionResult> ProxyDevOverrideAsync(string devOverrideUrl, string path)
    {
        if (!Uri.TryCreate(devOverrideUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { error = "Dev override URL must be an absolute http(s) URL." });
        }

        var client = _httpClientFactory.CreateClient(DevProxyClientName);
        try
        {
            var target = new Uri(baseUri, path);
            var upstream = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            if (!upstream.IsSuccessStatusCode)
                return StatusCode((int)upstream.StatusCode);

            var contentType = upstream.Content.Headers.ContentType?.ToString() ?? ResolveContentType(path);

            // Never cache dev content; edits must show on the next reload.
            Response.Headers.CacheControl = "no-store";
            ApplyFrameHeaders(contentType);

            var body = await upstream.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            return File(body, contentType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Dev override proxy failed for {Url}", devOverrideUrl);
            return StatusCode(502, new { error = "Dev override server is unreachable: " + ex.Message });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new { error = "Dev override server timed out." });
        }
    }

    private void ApplyResponseHeaders(string contentType)
    {
        // Versioned paths are write-once (duplicate versions are rejected at import),
        // so aggressive immutable caching is safe for everything except the HTML shell,
        // which must revalidate to pick up version changes promptly.
        Response.Headers.CacheControl = IsHtml(contentType)
            ? "no-cache"
            : "private, max-age=31536000, immutable";

        ApplyFrameHeaders(contentType);
    }

    private void ApplyFrameHeaders(string contentType)
    {
        if (!IsHtml(contentType)) return;

        // The global security middleware writes X-Frame-Options: DENY before MVC runs;
        // overwrite it so Location.razor's same-origin iframe may embed this page.
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        Response.Headers.ContentSecurityPolicy = "frame-ancestors 'self'";
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

    private static bool IsSafeSegment(string segment) =>
        segment.Length > 0 && !segment.Contains('/') && !segment.Contains('\\') &&
        !segment.Contains(':') && segment != "." && segment != "..";

    private static FileExtensionContentTypeProvider BuildContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".mjs"] = "text/javascript";
        provider.Mappings.TryAdd(".wasm", "application/wasm");
        return provider;
    }
}
