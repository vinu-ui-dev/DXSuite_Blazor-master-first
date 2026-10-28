using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Removes the cached thumbnail belonging to a deleted asset.
///
/// Thumbnails live in two stores: beside the media file for uploads, and in the tenant's App_Data
/// folder keyed by AssetId for web captures. The store is chosen from the asset's type, so callers
/// do not have to know which one applies.
///
/// Deleting an asset is a soft delete and thumbnails are a regenerable cache, so removing one is
/// recoverable — the backfill service rebuilds it if the row ever returns. The source media file is
/// deliberately left in place, because that is not recoverable.
/// </summary>
public class AssetThumbnailCleanupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ThumbnailService _thumbnails;
    private readonly WebPreviewService _webPreviews;
    private readonly ITenantMediaPathResolver _mediaPaths;
    private readonly ILogger<AssetThumbnailCleanupService> _logger;

    public AssetThumbnailCleanupService(
        IServiceScopeFactory scopeFactory,
        ThumbnailService thumbnails,
        WebPreviewService webPreviews,
        ITenantMediaPathResolver mediaPaths,
        ILogger<AssetThumbnailCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _thumbnails = thumbnails;
        _webPreviews = webPreviews;
        _mediaPaths = mediaPaths;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the thumbnail for an asset that has just been soft-deleted. Best-effort: any failure
    /// is logged and swallowed, because an orphaned thumbnail is a smaller problem than a delete
    /// that reports failure after the row is already marked deleted.
    /// </summary>
    public async Task CleanUpAsync(TblAsset asset)
    {
        try
        {
            if (asset.AssetTypeId == (int)Types.AssetType.Web)
            {
                _webPreviews.DeleteThumbnail(asset.AssetId);
                return;
            }

            if (string.IsNullOrWhiteSpace(asset.AssetPath)) return;

            // One media file can back more than one asset row, because content package imports
            // reuse paths, so the thumbnail only goes when the last row using it does.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
            var stillInUse = await db.TblAssets.AnyAsync(a =>
                a.AssetPath == asset.AssetPath && a.AssetId != asset.AssetId && !a.IsDeleted);

            if (stillInUse)
            {
                _logger.LogInformation(
                    "Kept the thumbnail for {AssetPath}: another asset still references that file", asset.AssetPath);
                return;
            }

            var localPath = ResolveLocalPath(asset.AssetPath);
            if (localPath != null)
                _thumbnails.DeleteThumbnail(localPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thumbnail cleanup failed for asset {AssetId}", asset.AssetId);
        }
    }

    /// <summary>
    /// Maps a stored web path such as /MediaAssets/5851/1080/name.png onto the tenant's media root.
    /// Returns null for anything that is not a site-relative path, which covers streams and any
    /// asset whose AssetPath is an absolute URL.
    /// </summary>
    private string? ResolveLocalPath(string assetPath)
    {
        if (!assetPath.StartsWith('/')) return null;

        var relative = assetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(_mediaPaths.GetRoot());
        var combined = Path.GetFullPath(Path.Combine(root, relative));

        // A stored path is not user input, but it is data, and a traversal inside one would put the
        // delete outside the media tree entirely.
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Ignoring asset path {AssetPath}: it resolves outside the media root", assetPath);
            return null;
        }

        return combined;
    }
}
