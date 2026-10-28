using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services;

public class AssetIngestService : IAssetIngestService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
    };

    private readonly IDirectorDBContext _db;
    private readonly IContentMetadataService _contentMetadataService;
    private readonly IApprovalChainService _approvalChainService;
    private readonly IAssetApprovalService _approvalService;
    private readonly IUploadIdentityService _uploadIdentity;
    private readonly ThumbnailService _thumbnailService;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly DXSuite.Shared.Multitenancy.ITenantMediaPathResolver _mediaPaths;
    private readonly ILogger<AssetIngestService> _logger;
    private readonly IPublicationInvalidator _publicationInvalidator;

    public AssetIngestService(
        IDirectorDBContext db,
        IContentMetadataService contentMetadataService,
        IApprovalChainService approvalChainService,
        IAssetApprovalService approvalService,
        IUploadIdentityService uploadIdentity,
        ThumbnailService thumbnailService,
        IVideoProcessingService videoProcessing,
        DXSuite.Shared.Multitenancy.ITenantMediaPathResolver mediaPaths,
        ILogger<AssetIngestService> logger,
        IPublicationInvalidator publicationInvalidator)
    {
        _publicationInvalidator = publicationInvalidator;
        _db = db;
        _contentMetadataService = contentMetadataService;
        _approvalChainService = approvalChainService;
        _approvalService = approvalService;
        _uploadIdentity = uploadIdentity;
        _thumbnailService = thumbnailService;
        _videoProcessing = videoProcessing;
        _mediaPaths = mediaPaths;
        _logger = logger;
    }

    public async Task<AssetIngestResult> IngestAsync(AssetIngestRequest request, CancellationToken ct = default)
    {
        var result = new AssetIngestResult();

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            result.Error = "Invalid file name.";
            return result;
        }

        // The identity is re-checked here, before anything is written: the pick may be a session old, and
        // an upload the user can no longer make as that identity fails rather than landing unscoped.
        var prepared = await _uploadIdentity.PrepareAsync(request.UploaderUserId, request.LocationId, request.UploadingAsUserGroupId, picks: null, ct);
        if (!prepared.IsValid)
        {
            result.Error = prepared.Error;
            result.IdentityCandidates = prepared.Candidates;
            return result;
        }

        var tags = BuildTags(prepared.Tags, request.CustomMetadata);

        // Same folder layout as MediaAssetUploader: MediaAssets/{locationId}[/{constraintId}]
        var folder = request.ConstraintId != 0
            ? Path.Combine(_mediaPaths.GetRoot(), "MediaAssets", request.LocationId.ToString(), request.ConstraintId.ToString())
            : Path.Combine(_mediaPaths.GetRoot(), "MediaAssets", request.LocationId.ToString());
        Directory.CreateDirectory(folder);

        var localPath = Path.Combine(folder, fileName);
        var webPath = request.ConstraintId != 0
            ? $"/MediaAssets/{request.LocationId}/{request.ConstraintId}/{fileName}"
            : $"/MediaAssets/{request.LocationId}/{fileName}";

        // The manifest builder reads MediaAssets/{locationId} directly and treats a media file with
        // no asset row as legacy content that predates approvals, which admits it unapproved. The
        // upload is therefore assembled in a staging folder the location watcher does not see, and
        // only moved into place once its rows exist.
        var stagingFolder = Path.Combine(_mediaPaths.GetRoot(), "MediaAssets", "_staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingFolder);
        var stagedPath = Path.Combine(stagingFolder, fileName);

        try
        {
            await using (var target = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                await request.Content.CopyToAsync(target, ct);
            }

            WriteSidecar(stagedPath, request.LocationId, fileName, tags);

            var asset = new TblAsset
            {
                AssetGroupId = 1,
                AssetName = fileName,
                AssetPath = webPath,
                AssetTypeId = VideoExtensions.Contains(Path.GetExtension(fileName))
                    ? (int)Types.AssetType.Video
                    : (int)Types.AssetType.Image,
                ConstraintId = request.ConstraintId,
                LocationId = request.LocationId,
                // Never invented: with no user behind the upload both stay null, which leaves the asset
                // to full access users.
                UploadedByUserId = prepared.OwnerUserId,
                UploadedByUserGroupId = prepared.OwnerGroupId
            };

            if (asset.AssetTypeId == (int)Types.AssetType.Video)
            {
                try
                {
                    var probe = await _videoProcessing.AnalyzeAsync(stagedPath);
                    if (probe?.Duration > 0)
                    {
                        asset.Duration = probe.Duration;
                        asset.AssetWidth = probe.Width;
                        asset.AssetHeight = probe.Height;
                        UpdateSidecarField(stagedPath, "Duration",
                            Math.Max(1, (int)Math.Round(probe.Duration)).ToString());
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Video duration probe failed for {Path}", stagedPath); }
            }
            _db.TblAssets.Add(asset);
            await _db.SaveChangesAsync(ct);
            result.AssetId = asset.AssetId;
            result.AssetPath = webPath;

            var setByUserId = request.UploaderUserId > 0 ? request.UploaderUserId : (int?)null;
            foreach (var (key, values) in tags)
                await _contentMetadataService.SetMetadataAsync(asset.AssetId, key, values, setByUserId);

            if (request.EnableContentApproval)
            {
                var approval = new TblAssetApproval
                {
                    AssetId = asset.AssetId,
                    UploadedByUserId = request.UploaderUserId,
                    ApprovalStatus = 0, // Pending
                    SubmittedDate = DateTime.UtcNow,
                    ExtendedData = System.Text.Json.JsonSerializer.Serialize(new { source = request.Source })
                };
                _db.TblAssetApprovals.Add(approval);
                await _db.SaveChangesAsync(ct);

                await _approvalChainService.SubmitToChainAsync(approval.AssetApprovalId, request.LocationId,
                    passedLlmModeration: false, uploadingUserId: request.UploaderUserId, pickedRuleId: prepared.PickedRuleId);

                UpdateSidecarField(stagedPath, "ApprovalStatus", approval.ApprovalStatus.ToString());
                result.ApprovalStatus = approval.ApprovalStatus;
            }

            // Reveal the upload. The sidecar moves first so the gate never sees the media file
            // without the approval state that belongs to it.
            if (File.Exists(stagedPath + ".metadata"))
            {
                if (File.Exists(localPath + ".metadata")) File.Delete(localPath + ".metadata");
                File.Move(stagedPath + ".metadata", localPath + ".metadata");
            }
            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(stagedPath, localPath);

            try { await _thumbnailService.GenerateThumbnailAsync(localPath); }
            catch (Exception ex) { _logger.LogError(ex, "Thumbnail generation failed for {Path}", localPath); }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            // Nothing was revealed unless the move completed, so the staged copy is simply dropped.
            _logger.LogError(ex, "Asset ingest failed for {FileName} at location {LocationId}", fileName, request.LocationId);
            result.Error = "Ingest failed: " + ex.Message;
            return result;
        }
        finally
        {
            try { if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, recursive: true); } catch { }
        }
    }

    public async Task<bool> DeleteAssetAsync(int assetId, CancellationToken ct = default)
    {
        var asset = await _db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == assetId, ct);
        if (asset == null) return false;

        // Mirror Playlist.razor's delete: withdraw pending approval, hard-delete the
        // row (playlist content is either present or gone), then remove files.
        await _approvalService.WithdrawPendingForAssetAsync(assetId);
        _db.TblAssets.Remove(asset);
        await _db.SaveChangesAsync(ct);
        _publicationInvalidator.AssetsChanged([asset.LocationId]);

        try
        {
            var localPath = Path.Combine(_mediaPaths.GetRoot(), asset.AssetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath)) File.Delete(localPath);
            if (File.Exists(localPath + ".metadata")) File.Delete(localPath + ".metadata");

            // Video thumbnails live in a Thumbnails/ subfolder as PNG
            var thumbnailPath = Path.Combine(
                Path.GetDirectoryName(localPath)!,
                "Thumbnails",
                Path.GetFileNameWithoutExtension(localPath) + ".png");
            if (File.Exists(thumbnailPath)) File.Delete(thumbnailPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete files for asset {AssetId}; DB row was removed", assetId);
        }

        return true;
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The tags an upload is written with: the identity's values, with any key the request names
    /// explicitly taking the request's value instead.
    /// </summary>
    internal static Dictionary<string, List<string>> BuildTags(
        IReadOnlyDictionary<string, List<string>> identityTags, IReadOnlyDictionary<string, string>? explicitTags)
    {
        var tags = new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer);
        foreach (var (key, values) in identityTags)
            tags[key] = values;
        foreach (var (key, value) in explicitTags ?? new Dictionary<string, string>())
        {
            var values = DXSuite.Shared.Services.Identity.MetadataValueSet.Normalize([value]);
            if (values.Count > 0)
                tags[MetadataEvaluator.NormalizeKey(key)] = values;
        }
        return tags;
    }

    /// <summary>
    /// Writes the XML .metadata sidecar in the same format as MediaAssetUploader's
    /// UpdateMetadataFile helper (usermetadata/field elements, ovx_String datatype).
    /// </summary>
    private void WriteSidecar(string localPath, int locationId, string fileName, Dictionary<string, List<string>> tags)
    {
        try
        {
            UpdateSidecarField(localPath, "Location", locationId.ToString());
            UpdateSidecarField(localPath, "FileName", fileName);

            // A sidecar field holds one string, so a multi-valued tag is comma-joined there; the database
            // keeps one row per value.
            foreach (var (key, values) in tags)
                UpdateSidecarField(localPath, key, string.Join(",", values));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write metadata sidecar for {Path}", localPath);
        }
    }

    internal static void UpdateSidecarField(string diskPath, string name, string value)
    {
        var metadataFilePath = diskPath + ".metadata";

        if (File.Exists(metadataFilePath))
        {
            var doc = new XmlDocument();
            doc.Load(metadataFilePath);
            var root = doc.SelectSingleNode("usermetadata");
            if (root == null) return;

            foreach (XmlNode line in root.ChildNodes)
            {
                if (line.Attributes?["name"]?.Value == name)
                {
                    line.Attributes["value"]!.Value = value;
                    doc.Save(metadataFilePath);
                    return;
                }
            }

            var element = doc.CreateElement("field");
            element.SetAttribute("name", name);
            element.SetAttribute("datatype", "ovx_String");
            element.SetAttribute("value", value);
            root.AppendChild(element);
            doc.Save(metadataFilePath);
        }
        else
        {
            var doc = new XmlDocument();
            var root = doc.CreateElement("usermetadata");
            doc.AppendChild(root);
            var element = doc.CreateElement("field");
            element.SetAttribute("name", name);
            element.SetAttribute("datatype", "ovx_String");
            element.SetAttribute("value", value);
            root.AppendChild(element);
            doc.Save(metadataFilePath);
        }
    }
}
