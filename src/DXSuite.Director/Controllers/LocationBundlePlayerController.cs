using System.Security.Cryptography;
using System.Text;
using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.LocationBundles;
using DXSuite.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Serves a location bundle's published state to external players (signage devices,
/// Omnivex Moxie scripts, integrations). This is the read side of the bundle publish
/// channel: a bundle writes JSON through the cookie-authenticated Location Content API
/// under the "public." key prefix, and those keys — and only those — are readable here
/// with an API key. Every other storage key stays private to the bundle's own UI.
///
/// The channel is deliberately schema-agnostic: the host never interprets the document,
/// so a new bundle publishes to a new "public.*" key with no host change at all.
/// </summary>
[Route("api/player/bundle-state")]
[ApiController]
[Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
public class LocationBundlePlayerController : ControllerBase
{
    private const int StorageKeyMaxLength = 128;

    private readonly IDirectorDBContext _db;
    private readonly IOptions<FeatureFlags> _featureFlags;

    public LocationBundlePlayerController(IDirectorDBContext db, IOptions<FeatureFlags> featureFlags)
    {
        _db = db;
        _featureFlags = featureFlags;
    }

    [HttpGet("{locationId:int}/{key}")]
    public async Task<IActionResult> GetPublishedState(int locationId, string key)
    {
        // Panels publish through this same channel without necessarily having the bundle
        // runtime enabled, so either flag opens the read side.
        if (!_featureFlags.Value.LocationBundles && !_featureFlags.Value.Panels) return NotFound();

        if (this.CheckScope(ApiKeyScopes.SignageRead) is { } denied) return denied;

        // Honour a per-key location restriction when one is set. Keys created through the
        // admin UI are unrestricted, so this is usually a no-op — but a key restricted
        // out-of-band then works without a code change.
        var keyLocationId = HttpContext.Items["ApiKeyLocationId"] as int?;
        if (keyLocationId.HasValue && keyLocationId.Value != locationId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "API key is not authorized for this location." });

        // 404 rather than 403 for a non-public key: a private key's existence must not be
        // confirmed to a caller that can never read it.
        if (!IsReadablePublicKey(key)) return NotFound();

        // The storage row is FK'd to tblLocation with cascade delete, so a hit proves a live
        // location — no separate existence query is needed on this polled path.
        var value = await _db.TblLocationBundleStorages
            .AsNoTracking()
            .Where(s => s.LocationId == locationId && s.StorageKey == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (value == null) return NotFound();

        // ETag is a hash of the content rather than LastEditDate: that column defaults to
        // GETDATE() (server local) while the API writes DateTime.UtcNow, so a timestamp
        // comparison would be wrong by the server's UTC offset.
        var etag = ComputeETag(value);
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache";

        if (RequestMatchesETag(etag))
            return StatusCode(StatusCodes.Status304NotModified);

        return Content(value, "application/json");
    }

    private bool RequestMatchesETag(string etag)
    {
        foreach (var header in Request.Headers.IfNoneMatch)
        {
            if (string.IsNullOrEmpty(header)) continue;

            foreach (var candidate in header.Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || trimmed == etag) return true;

                // Tolerate a weak validator prefix; the payload is byte-identical either way.
                if (trimmed.StartsWith("W/", StringComparison.Ordinal) && trimmed[2..] == etag)
                    return true;
            }
        }

        return false;
    }

    private static string ComputeETag(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"\"{Convert.ToBase64String(hash)}\"";
    }

    /// <summary>
    /// A readable key is public by prefix and free of anything that could confuse routing
    /// or storage lookup. Length mirrors the storage column so an over-long key fails here
    /// rather than silently missing the index.
    /// </summary>
    private static bool IsReadablePublicKey(string key) =>
        LocationBundleConstants.IsPublicStorageKey(key) &&
        key.Length <= StorageKeyMaxLength &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
