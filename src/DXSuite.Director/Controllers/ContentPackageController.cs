using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.ContentPackage;
using DXSuite.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Multi-environment content content package endpoints (bundle-based). Gated by
/// Features:ContentPackage — when the flag is off, every action returns 404.
/// </summary>
[Route("api/content-package")]
[ApiController]
[Authorize]
public class ContentPackageController : ControllerBase
{
    private readonly IContentPackageBundleBuilder _bundleBuilder;
    private readonly IContentPackageBundleImporter _bundleImporter;
    private readonly IContentPackageHistoryService _historyService;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<ContentPackageController> _logger;

    /// <summary>
    /// Pending bundle downloads keyed by a one-time token. The Blazor component
    /// builds the bundle via direct service call, stages it as a temp file, then
    /// redirects the browser here for a streaming download — avoiding the SignalR
    /// message-size limit that blocks large base64 payloads through JS interop.
    /// Entries auto-expire after 10 minutes.
    /// </summary>
    internal static readonly ConcurrentDictionary<Guid, PendingDownload> PendingDownloads = new();

    internal sealed class PendingDownload
    {
        public required string FilePath { get; init; }
        public required string FileName { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public ContentPackageController(
        IContentPackageBundleBuilder bundleBuilder,
        IContentPackageBundleImporter bundleImporter,
        IContentPackageHistoryService historyService,
        IOptions<FeatureFlags> featureFlags,
        ILogger<ContentPackageController> logger)
    {
        _bundleBuilder = bundleBuilder;
        _bundleImporter = bundleImporter;
        _historyService = historyService;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    /// <summary>
    /// Builds a content package bundle from the supplied selection and streams the resulting
    /// ZIP back to the caller.
    /// </summary>
    [HttpPost("bundle/export")]
    public async Task<IActionResult> ExportBundle([FromBody] ContentPackageSelection selection)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();

        if (selection == null ||
            (selection.LocationContentPackageIds.Count == 0 &&
             selection.AssetContentPackageIds.Count == 0 &&
             selection.DisplayContentPackageIds.Count == 0))
        {
            return BadRequest(new { error = "Selection is empty." });
        }

        try
        {
            using var ms = new MemoryStream();
            var result = await _bundleBuilder.BuildAsync(selection, User.Identity?.Name, ms, HttpContext.RequestAborted);
            ms.Position = 0;

            var fileName = result.SuggestedFileName ?? $"content-package-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
            Response.Headers["X-ContentPackage-Records"] = result.TotalRecords.ToString();
            Response.Headers["X-ContentPackage-Media-Files"] = result.MediaFileCount.ToString();

            return File(ms.ToArray(), "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export content package bundle");
            return StatusCode(500, new { error = "Failed to export content package bundle." });
        }
    }

    /// <summary>
    /// Streams a previously-built bundle to the browser. The token is single-use;
    /// the temp file is deleted after the response completes.
    /// </summary>
    [HttpGet("bundle/download/{token:guid}")]
    public IActionResult DownloadBundle(Guid token)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();

        // Expire stale entries while we're here
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kvp in PendingDownloads)
        {
            if (kvp.Value.CreatedAt < cutoff && PendingDownloads.TryRemove(kvp.Key, out var stale))
            {
                try { System.IO.File.Delete(stale.FilePath); } catch { /* best-effort */ }
            }
        }

        if (!PendingDownloads.TryRemove(token, out var download))
            return NotFound(new { error = "Download token not found or already used." });

        var stream = new FileStream(download.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete,
            bufferSize: 81920, options: FileOptions.DeleteOnClose | FileOptions.SequentialScan);

        return File(stream, "application/zip", download.FileName);
    }

    /// <summary>
    /// Stages an uploaded bundle and returns a preview of the changes the apply step
    /// will make.
    /// </summary>
    [HttpPost("bundle/import")]
    [RequestSizeLimit(2_147_483_648)] // 2 GiB
    public async Task<IActionResult> ImportBundle(IFormFile bundle)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();
        if (bundle == null || bundle.Length == 0) return BadRequest(new { error = "No bundle uploaded." });

        try
        {
            await using var stream = bundle.OpenReadStream();
            var preview = await _bundleImporter.CreatePreviewAsync(stream, bundle.FileName, HttpContext.RequestAborted);
            return Ok(preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stage content package bundle for preview");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class ApplyBundleRequest
    {
        public Guid StagingId { get; set; }

        /// <summary>
        /// When true, after upserting the bundle the importer deletes any child
        /// records of the bundle's locations whose ContentPackageIds are not in the
        /// bundle. Destructive — use only when the target should exactly mirror
        /// the source within the promoted location scope.
        /// </summary>
        public bool ReplaceMode { get; set; }
    }

    /// <summary>
    /// Applies a previously-staged bundle by its stagingId.
    /// </summary>
    [HttpPost("bundle/apply")]
    public async Task<IActionResult> ApplyBundle([FromBody] ApplyBundleRequest request)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();
        if (request == null || request.StagingId == Guid.Empty) return BadRequest(new { error = "stagingId is required." });

        try
        {
            var result = await _bundleImporter.ApplyAsync(request.StagingId, User.Identity?.Name, request.ReplaceMode, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply content package bundle {StagingId}", request.StagingId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Discards a staged bundle without applying it.
    /// </summary>
    [HttpPost("bundle/cancel/{stagingId:guid}")]
    public IActionResult CancelPreview(Guid stagingId)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();
        return _bundleImporter.CancelPreview(stagingId) ? Ok() : NotFound();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // History — read-only audit log queries for the UI.
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("history")]
    public async Task<IActionResult> ListHistory([FromQuery] string? direction = null, [FromQuery] int take = 100)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();
        var rows = await _historyService.ListAsync(direction, take, HttpContext.RequestAborted);
        return Ok(rows);
    }

    [HttpGet("history/{id:int}")]
    public async Task<IActionResult> GetHistoryDetail(int id)
    {
        if (!_featureFlags.Value.ContentPackage) return NotFound();
        var detail = await _historyService.GetDetailAsync(id, HttpContext.RequestAborted);
        return detail == null ? NotFound() : Ok(detail);
    }
}
