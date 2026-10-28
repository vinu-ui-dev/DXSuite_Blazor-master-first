using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

public class ContentOverwriteResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool PendingApproval { get; set; }
    public string? NewDiskPath { get; set; }
}

public interface IContentOverwriteService
{
    /// <summary>
    /// Replaces a playlist media file with new content while keeping its schedule metadata,
    /// play order, weight, and access tags. The replacement always gets a fresh filename so
    /// players detect the change through the normal removed+added manifest diff; replacing
    /// in place under the same name is invisible to player caches.
    /// When requireReapproval is set (and the location runs approval), the new file lands as
    /// a pending upload carrying the old file's metadata and the swap completes on approval.
    /// </summary>
    Task<ContentOverwriteResult> OverwritePlaylistFileAsync(
        int locationId, string oldDiskPath, Stream newContent, string newOriginalFileName,
        int userId, bool requireReapproval);

    /// <summary>
    /// Replaces a library/schedule asset's file while keeping the same tblAsset row, so every
    /// ContentAssignment referencing the AssetId keeps working untouched.
    /// </summary>
    Task<ContentOverwriteResult> OverwriteLibraryAssetAsync(
        int assetId, Stream newContent, string newOriginalFileName, int userId);

    /// <summary>
    /// Completes a staged replacement after its approval finalizes: removes the superseded
    /// file and asset row named in the approval's replace marker.
    /// </summary>
    Task CompletePendingReplaceAsync(TblAssetApproval approval);
}

public class ContentOverwriteService : IContentOverwriteService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };

    private readonly IDirectorDBContext _db;
    private readonly ThumbnailService _thumbnailService;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly IServiceProvider _services;
    private readonly ITenantMediaPathResolver _mediaPaths;
    private readonly ILogger<ContentOverwriteService> _logger;
    private readonly IPublicationInvalidator _publicationInvalidator;

    public ContentOverwriteService(
        IDirectorDBContext db,
        ThumbnailService thumbnailService,
        IVideoProcessingService videoProcessing,
        IServiceProvider services,
        ITenantMediaPathResolver mediaPaths,
        IPublicationInvalidator publicationInvalidator,
        ILogger<ContentOverwriteService> logger)
    {
        _db = db;
        _thumbnailService = thumbnailService;
        _videoProcessing = videoProcessing;
        // The approval chain resolves lazily: ApprovalChainService depends on
        // IAssetApprovalService, which depends on this service, so a constructor
        // dependency would be a DI cycle
        _services = services;
        _mediaPaths = mediaPaths;
        _publicationInvalidator = publicationInvalidator;
        _logger = logger;
    }

    public async Task<ContentOverwriteResult> OverwritePlaylistFileAsync(
        int locationId, string oldDiskPath, Stream newContent, string newOriginalFileName,
        int userId, bool requireReapproval)
    {
        var validation = ValidateFileName(newOriginalFileName);
        if (validation != null) return new ContentOverwriteResult { Error = validation };
        if (!File.Exists(oldDiskPath))
            return new ContentOverwriteResult { Error = "The original file no longer exists." };

        var folder = Path.GetDirectoryName(oldDiskPath)!;
        var newFileName = BuildUniqueFileName(newOriginalFileName);
        var newDiskPath = Path.Combine(folder, newFileName);
        var oldWebPath = ToWebPath(oldDiskPath);
        var newWebPath = ToWebPath(newDiskPath);

        try
        {
            await WriteFileAsync(newDiskPath, newContent);

            // Carry the old sidecar over wholesale (dates, days, PlayOrder, Weight, access
            // tags), then correct the identity fields
            if (File.Exists(oldDiskPath + ".metadata"))
                File.Copy(oldDiskPath + ".metadata", newDiskPath + ".metadata", overwrite: true);
            AssetIngestService.UpdateSidecarField(newDiskPath, "Location", locationId.ToString());
            AssetIngestService.UpdateSidecarField(newDiskPath, "FileName", Path.GetFileName(newOriginalFileName));

            var probe = await ProbeIfVideoAsync(newDiskPath);
            try { await _thumbnailService.GenerateThumbnailAsync(newDiskPath); }
            catch (Exception ex) { _logger.LogWarning(ex, "Thumbnail generation failed for {Path}", newDiskPath); }

            var oldAsset = await _db.TblAssets.FirstOrDefaultAsync(a =>
                a.LocationId == locationId && a.AssetPath == oldWebPath && !a.IsDeleted);

            if (requireReapproval)
            {
                // Staged swap: the pending sidecar status keeps the replacement out of the
                // manifest while the old file plays on; approval completes the swap
                AssetIngestService.UpdateSidecarField(newDiskPath, "ApprovalStatus",
                    ((int)ApprovalStatus.Pending).ToString());

                // Replacement changes the bytes, not who owns the content or how it is targeted: taking the
                // replacer's identity would let a full access user silently take content from its owner.
                var newAsset = new TblAsset
                {
                    AssetGroupId = 1,
                    AssetName = Path.GetFileName(newOriginalFileName),
                    AssetPath = newWebPath,
                    AssetTypeId = ResolveAssetTypeId(newDiskPath),
                    LocationId = locationId,
                    UploadedByUserId = oldAsset?.UploadedByUserId,
                    UploadedByUserGroupId = oldAsset?.UploadedByUserGroupId
                };
                ApplyProbe(newAsset, probe);
                _db.TblAssets.Add(newAsset);
                await _db.SaveChangesAsync();

                // Written directly rather than through the tag service: the replacement is pending, so no
                // publication holds it and there is nothing to invalidate.
                if (oldAsset != null)
                {
                    var oldTags = await _db.TblAssetContentMetadata.AsNoTracking()
                        .Where(t => t.AssetId == oldAsset.AssetId)
                        .ToListAsync();
                    foreach (var tag in oldTags)
                    {
                        _db.TblAssetContentMetadata.Add(new TblAssetContentMetadata
                        {
                            AssetId = newAsset.AssetId,
                            MetadataKey = tag.MetadataKey,
                            MetadataValue = tag.MetadataValue,
                            SetByUserId = tag.SetByUserId,
                            CreateDate = DateTime.UtcNow
                        });
                    }
                    if (oldTags.Count > 0)
                        await _db.SaveChangesAsync();
                }

                var approval = new TblAssetApproval
                {
                    AssetId = newAsset.AssetId,
                    UploadedByUserId = userId,
                    ApprovalStatus = (int)ApprovalStatus.Pending,
                    SubmittedDate = DateTime.UtcNow,
                    ExtendedData = JsonSerializer.Serialize(new
                    {
                        source = "ContentOverwrite",
                        replacesDiskPath = oldDiskPath,
                        replacesAssetId = oldAsset?.AssetId
                    })
                };
                _db.TblAssetApprovals.Add(approval);
                await _db.SaveChangesAsync();
                _publicationInvalidator.AssetsChanged([locationId]);

                var approvalChain = _services.GetRequiredService<IApprovalChainService>();
                await approvalChain.SubmitToChainAsync(approval.AssetApprovalId, locationId,
                    passedLlmModeration: false, uploadingUserId: userId);

                await WriteEventLogAsync(locationId, oldAsset?.AssetId, oldDiskPath, newDiskPath, userId, staged: true);
                return new ContentOverwriteResult { Success = true, PendingApproval = true, NewDiskPath = newDiskPath };
            }

            if (oldAsset != null)
            {
                oldAsset.AssetPath = newWebPath;
                oldAsset.AssetName = Path.GetFileName(newOriginalFileName);
                oldAsset.AssetTypeId = ResolveAssetTypeId(newDiskPath);
                ApplyProbe(oldAsset, probe);
                await _db.SaveChangesAsync();
                _publicationInvalidator.AssetsChanged([oldAsset.LocationId]);
            }

            DeleteMediaFiles(oldDiskPath);
            await WriteEventLogAsync(locationId, oldAsset?.AssetId, oldDiskPath, newDiskPath, userId, staged: false);
            return new ContentOverwriteResult { Success = true, NewDiskPath = newDiskPath };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playlist overwrite failed for {OldPath}", oldDiskPath);
            try { if (File.Exists(newDiskPath)) File.Delete(newDiskPath); } catch { }
            try { if (File.Exists(newDiskPath + ".metadata")) File.Delete(newDiskPath + ".metadata"); } catch { }
            return new ContentOverwriteResult { Error = "Replace failed: " + ex.Message };
        }
    }

    public async Task<ContentOverwriteResult> OverwriteLibraryAssetAsync(
        int assetId, Stream newContent, string newOriginalFileName, int userId)
    {
        var validation = ValidateFileName(newOriginalFileName);
        if (validation != null) return new ContentOverwriteResult { Error = validation };

        var asset = await _db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == assetId && !a.IsDeleted);
        if (asset == null) return new ContentOverwriteResult { Error = "Asset not found." };
        if (asset.AssetTypeId != (int)AssetType.Image && asset.AssetTypeId != (int)AssetType.Video)
            return new ContentOverwriteResult { Error = "Only image and video assets can be replaced." };

        var oldDiskPath = ToDiskPath(asset.AssetPath);
        var folder = Path.GetDirectoryName(oldDiskPath)!;
        Directory.CreateDirectory(folder);
        var newFileName = BuildUniqueFileName(newOriginalFileName);
        var newDiskPath = Path.Combine(folder, newFileName);

        try
        {
            await WriteFileAsync(newDiskPath, newContent);

            if (File.Exists(oldDiskPath + ".metadata"))
                File.Copy(oldDiskPath + ".metadata", newDiskPath + ".metadata", overwrite: true);
            AssetIngestService.UpdateSidecarField(newDiskPath, "FileName", Path.GetFileName(newOriginalFileName));

            var probe = await ProbeIfVideoAsync(newDiskPath);
            try { await _thumbnailService.GenerateThumbnailAsync(newDiskPath); }
            catch (Exception ex) { _logger.LogWarning(ex, "Thumbnail generation failed for {Path}", newDiskPath); }

            // Same row, new file: ContentAssignment JSON references the AssetId and never changes
            asset.AssetPath = ToWebPath(newDiskPath);
            asset.AssetName = Path.GetFileName(newOriginalFileName);
            asset.AssetTypeId = ResolveAssetTypeId(newDiskPath);
            ApplyProbe(asset, probe);
            await _db.SaveChangesAsync();
            _publicationInvalidator.AssetsChanged([asset.LocationId]);

            DeleteMediaFiles(oldDiskPath);
            await WriteEventLogAsync(asset.LocationId, asset.AssetId, oldDiskPath, newDiskPath, userId, staged: false);
            return new ContentOverwriteResult { Success = true, NewDiskPath = newDiskPath };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library asset overwrite failed for asset {AssetId}", assetId);
            try { if (File.Exists(newDiskPath)) File.Delete(newDiskPath); } catch { }
            try { if (File.Exists(newDiskPath + ".metadata")) File.Delete(newDiskPath + ".metadata"); } catch { }
            return new ContentOverwriteResult { Error = "Replace failed: " + ex.Message };
        }
    }

    public async Task CompletePendingReplaceAsync(TblAssetApproval approval)
    {
        if (string.IsNullOrEmpty(approval.ExtendedData)) return;

        string? replacesDiskPath = null;
        int? replacesAssetId = null;
        try
        {
            using var doc = JsonDocument.Parse(approval.ExtendedData);
            if (doc.RootElement.TryGetProperty("replacesDiskPath", out var pathProp))
                replacesDiskPath = pathProp.GetString();
            if (doc.RootElement.TryGetProperty("replacesAssetId", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                replacesAssetId = idProp.GetInt32();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(replacesDiskPath)) return;

        try
        {
            DeleteMediaFiles(replacesDiskPath);

            if (replacesAssetId.HasValue)
            {
                var oldAsset = await _db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == replacesAssetId.Value);
                if (oldAsset != null)
                {
                    _db.TblAssets.Remove(oldAsset);
                    await _db.SaveChangesAsync();
                    _publicationInvalidator.AssetsChanged([oldAsset.LocationId]);
                }
            }

            _logger.LogInformation("Completed staged content replacement: removed {OldPath}", replacesDiskPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete staged replacement for approval {ApprovalId}", approval.AssetApprovalId);
        }
    }

    // ────────────────────────────────────────────────────────────────────────

    private static string? ValidateFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)) return "Invalid file name.";
        var extension = Path.GetExtension(name);
        if (!VideoExtensions.Contains(extension) && !ImageExtensions.Contains(extension))
            return "Replacement must be an image or video file.";
        return null;
    }

    private static string BuildUniqueFileName(string originalFileName)
    {
        var safeName = Path.GetFileName(originalFileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');
        return $"{Guid.NewGuid().ToString("N")[..8]}_{safeName}";
    }

    private static async Task WriteFileAsync(string path, Stream content)
    {
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(target);
    }

    private static int ResolveAssetTypeId(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path)) ? (int)AssetType.Video : (int)AssetType.Image;

    private async Task<VideoAnalysisResult?> ProbeIfVideoAsync(string path)
    {
        if (!VideoExtensions.Contains(Path.GetExtension(path))) return null;
        try
        {
            var probe = await _videoProcessing.AnalyzeAsync(path);
            if (probe?.Duration > 0)
                AssetIngestService.UpdateSidecarField(path, "Duration",
                    Math.Max(1, (int)Math.Round(probe.Duration)).ToString());
            return probe;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Video duration probe failed for {Path}", path);
            return null;
        }
    }

    private static void ApplyProbe(TblAsset asset, VideoAnalysisResult? probe)
    {
        if (probe?.Duration > 0)
        {
            asset.Duration = probe.Duration;
            asset.AssetWidth = probe.Width;
            asset.AssetHeight = probe.Height;
        }
        else if (asset.AssetTypeId == (int)AssetType.Image)
        {
            asset.Duration = 7.0;
        }
    }

    private string ToWebPath(string diskPath)
    {
        var root = Path.GetFullPath(_mediaPaths.GetRoot());
        var relative = Path.GetRelativePath(root, Path.GetFullPath(diskPath));
        return "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ToDiskPath(string webPath) =>
        Path.Combine(_mediaPaths.GetRoot(), webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static void DeleteMediaFiles(string diskPath)
    {
        try { if (File.Exists(diskPath)) File.Delete(diskPath); } catch { }
        try { if (File.Exists(diskPath + ".metadata")) File.Delete(diskPath + ".metadata"); } catch { }
        try
        {
            var thumbnail = Path.Combine(Path.GetDirectoryName(diskPath)!, "Thumbnails",
                Path.GetFileNameWithoutExtension(diskPath) + ".png");
            if (File.Exists(thumbnail)) File.Delete(thumbnail);
        }
        catch { }
    }

    private async Task WriteEventLogAsync(int locationId, int? assetId, string oldDiskPath, string newDiskPath,
        int userId, bool staged)
    {
        try
        {
            var infoSeverity = await _db.TblEventSeverities.FirstOrDefaultAsync(s => s.SeverityName == "INFO");
            _db.TblEventLogs.Add(new TblEventLog
            {
                EventSource = "ContentOverwrite",
                EventTypeId = 1,
                EventTitle = staged ? "Content replacement submitted for approval" : "Content replaced",
                EventMessage = $"{Path.GetFileName(oldDiskPath)} replaced by {Path.GetFileName(newDiskPath)}",
                EventSeverityId = infoSeverity?.EventSeverityId ?? 1,
                UserId = userId,
                AssetId = assetId,
                LocationId = locationId,
                CreateDate = DateTime.UtcNow,
                Details = JsonSerializer.Serialize(new
                {
                    oldFile = Path.GetFileName(oldDiskPath),
                    newFile = Path.GetFileName(newDiskPath),
                    locationId,
                    staged
                })
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write overwrite event log for {Path}", oldDiskPath);
        }
    }
}
