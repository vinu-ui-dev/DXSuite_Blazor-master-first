using System;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Models.Panels;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Panels;
using DXSuite.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Data API for an anonymous panel. Lives under the same /panel/{key}/{token}/ prefix as the
/// files so the bundle derives its API base from its own path and never has the token injected
/// separately.
///
/// This is the entire security surface of the Panels feature. Authorization is an explicit
/// allowlist of four operations against exactly one location, not a general capability granted
/// by holding a credential. DXME-60 is the cautionary case: an api key with only signage:read
/// could DELETE bundles because "presented a key" was treated as "is authorized".
///
/// "api" is therefore a reserved path segment inside a panel bundle. The literal route wins
/// over PanelController's catch-all, so a bundle folder named "api" would be unreachable.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("panel/{panelKey}/{token}/api")]
public class PanelApiController : ControllerBase
{
    /// <summary>Minimum gap between writes from one panel. A human tapping a screen cannot
    /// legitimately exceed this, and it caps damage if a token leaks.</summary>
    private static readonly TimeSpan WriteInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPanelService _panels;
    private readonly IStatusSelectorService _statusSelector;
    private readonly IDirectorDBContext _db;
    private readonly IMemoryCache _cache;
    private readonly WebPreviewService _webPreview;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<PanelApiController> _logger;

    public PanelApiController(
        IPanelService panels,
        IStatusSelectorService statusSelector,
        IDirectorDBContext db,
        IMemoryCache cache,
        WebPreviewService webPreview,
        IOptions<FeatureFlags> featureFlags,
        ILogger<PanelApiController> logger)
    {
        _panels = panels;
        _statusSelector = statusSelector;
        _db = db;
        _cache = cache;
        _webPreview = webPreview;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    private string SiteUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    [HttpGet("context")]
    public async Task<IActionResult> GetContext(string panelKey, string token)
    {
        var (panel, error) = await AuthorizeAsync(panelKey, token);
        if (error != null) return error;

        return Ok(await _panels.GetContextAsync(panel!, HttpContext.RequestAborted));
    }

    [HttpGet("assets")]
    public async Task<IActionResult> GetAssets(string panelKey, string token)
    {
        var (panel, error) = await AuthorizeAsync(panelKey, token);
        if (error != null) return error;

        return Ok(await _panels.GetAssetsAsync(panel!, SiteUrl, HttpContext.RequestAborted));
    }

    [HttpGet("displays")]
    public async Task<IActionResult> GetDisplays(string panelKey, string token)
    {
        var (panel, error) = await AuthorizeAsync(panelKey, token);
        if (error != null) return error;

        return Ok(await _panels.GetDisplayStatesAsync(panel!, SiteUrl, HttpContext.RequestAborted));
    }

    /// <summary>
    /// Streams a web asset's captured thumbnail.
    ///
    /// Web captures live in tenant-scoped App_Data and are served by WebThumbnailController,
    /// which is [Authorize] — a panel is anonymous, so it would get 401 and every web link would
    /// render its fallback forever. This is the same file behind the panel's own credential
    /// rather than a session, scoped to assets in this panel's library so the token cannot be
    /// used to enumerate captures belonging to other locations.
    /// </summary>
    [HttpGet("assets/{assetId:int}/thumbnail")]
    public async Task<IActionResult> GetAssetThumbnail(string panelKey, string token, int assetId)
    {
        var (panel, error) = await AuthorizeAsync(panelKey, token);
        if (error != null) return error;

        if (!await IsSelectableAssetAsync(panel!, assetId)) return NotFound();

        var path = _webPreview.GetThumbnailLocalPath(assetId);
        if (!System.IO.File.Exists(path)) return NotFound();

        // Captures are regenerated in place under a stable name, so revalidation rather than a
        // long max-age; the panel re-requests these on every library refresh.
        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(path, "image/png");
    }

    /// <summary>
    /// Assigns an asset to one screen, or clears it when AssetId is null. Writes through
    /// IStatusSelectorService so a panel tap produces an ordinary DefaultContent schedule
    /// event: the same record the Director UI writes, visible on the Display page, and
    /// resolved by the normal player path.
    /// </summary>
    [HttpPut("displays/{displayId:int}/content")]
    public async Task<IActionResult> SetContent(
        string panelKey, string token, int displayId, [FromBody] PanelSetContentRequest request)
    {
        var (panel, error) = await AuthorizeAsync(panelKey, token);
        if (error != null) return error;

        // Authority stops at the panel's own hotspot map. Without this a valid token could
        // drive any display in the instance by id.
        if (!_panels.IsMappedDisplay(panel!, displayId))
            return NotFound();

        var rateKey = $"panel:write:{panel!.PanelId}";
        if (_cache.TryGetValue(rateKey, out _))
            return StatusCode(StatusCodes.Status429TooManyRequests);
        _cache.Set(rateKey, true, WriteInterval);

        var assetId = request?.AssetId;

        if (assetId.HasValue)
        {
            // Re-validate against the panel's own library rather than trusting the id: a token
            // holder must not be able to put arbitrary tenant content on a customer's wall.
            var permitted = await IsSelectableAssetAsync(panel!, assetId.Value);
            if (!permitted) return BadRequest(new { error = "Asset is not selectable on this panel." });

            await _statusSelector.SetActiveContentAsync(
                displayId, assetId.Value, null, $"panel:{panel!.PanelKey}");
        }
        else
        {
            await _statusSelector.ClearActiveContentAsync(displayId, $"panel:{panel!.PanelKey}");
        }

        // Publishing is the host's job, not the bundle's, so a panel that dies mid-interaction
        // cannot leave Moxie reading a document that disagrees with Director.
        await _panels.PublishAsync(panel!, SiteUrl, HttpContext.RequestAborted);

        _logger.LogInformation("Panel {PanelKey} set display {DisplayId} to asset {AssetId}",
            panel!.PanelKey, displayId, assetId);

        return Ok(await _panels.GetDisplayStatesAsync(panel!, SiteUrl, HttpContext.RequestAborted));
    }

    // ────────────────────────────────────────────────────────────────────────

    private async Task<(TblPanel? Panel, IActionResult? Error)> AuthorizeAsync(string panelKey, string token)
    {
        if (!_featureFlags.Value.Panels)
            return (null, NotFound());

        var panel = await _panels.ResolveAsync(panelKey, token, HttpContext.RequestAborted);
        if (panel == null)
            return (null, NotFound());

        await _panels.TouchLastSeenAsync(
            panel, HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.RequestAborted);

        return (panel, null);
    }

    /// <summary>
    /// Mirrors the filter GetAssetsAsync applies, so anything the panel could not see is also
    /// something it cannot assign.
    /// </summary>
    private async Task<bool> IsSelectableAssetAsync(TblPanel panel, int assetId)
    {
        if (!panel.AssetLibraryLocationId.HasValue) return false;

        var asset = await _db.TblAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AssetId == assetId
                                   && a.LocationId == panel.AssetLibraryLocationId.Value
                                   && !a.IsDeleted,
                                 HttpContext.RequestAborted);
        if (asset == null) return false;

        var approval = await _db.TblAssetApprovals
            .AsNoTracking()
            .Where(ap => ap.AssetId == assetId)
            .OrderByDescending(ap => ap.SubmittedDate)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        return approval == null || approval.ApprovalStatus == (int)Models.Types.ApprovalStatus.Approved;
    }
}
