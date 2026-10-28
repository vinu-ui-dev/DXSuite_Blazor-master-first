using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using static DXSuite.Director.Models.Types;
using System.Text.Json;
using System.Xml;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// Service for managing hotel content approval workflow.
    /// Handles creation, approval, and decline of hotel-uploaded content.
    /// </summary>
    public class AssetApprovalService : IAssetApprovalService
    {
        private readonly IDirectorDBContext _dbContext;
        private readonly ILogger<AssetApprovalService> _logger;
        private readonly IConfiguration _configuration;
        private readonly DXSuite.Shared.Multitenancy.ITenantMediaPathResolver _mediaPaths;
        private readonly IContentOverwriteService _contentOverwrite;
        private readonly DXSuite.Shared.Multitenancy.ITenantContext _tenantContext;
        private readonly IManifestCacheService _manifestCache;

        public AssetApprovalService(IDirectorDBContext dbContext, ILogger<AssetApprovalService> logger, IConfiguration configuration, DXSuite.Shared.Multitenancy.ITenantMediaPathResolver mediaPaths, IContentOverwriteService contentOverwrite, DXSuite.Shared.Multitenancy.ITenantContext tenantContext, IManifestCacheService manifestCache)
        {
            _dbContext = dbContext;
            _logger = logger;
            _configuration = configuration;
            _mediaPaths = mediaPaths;
            _contentOverwrite = contentOverwrite;
            _tenantContext = tenantContext;
            _manifestCache = manifestCache;
        }

        /// <summary>
        /// Drops the location's cached manifests so an approval decision reaches players on their
        /// next poll. Without this the decision waits out the cache TTL, which lets declined
        /// content keep playing after it was pulled.
        /// </summary>
        private void InvalidateManifestForLocation(int? locationId)
        {
            if (locationId is null) return;
            _manifestCache.InvalidateLocation(_tenantContext.TenantId, locationId.Value);
        }

        public async Task<List<TblAssetApproval>> GetPendingApprovalsAsync()
        {
            return await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .Include(a => a.UploadedByUser)
                .Where(a => a.ApprovalStatus == (int)ApprovalStatus.Pending)
                .OrderBy(a => a.SubmittedDate)
                .ToListAsync();
        }

        public async Task<List<TblAssetApproval>> GetApprovalsByStatusAsync(ApprovalStatus status)
        {
            return await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .Include(a => a.UploadedByUser)
                .Include(a => a.ReviewedByUser)
                .Where(a => a.ApprovalStatus == (int)status)
                .OrderByDescending(a => a.SubmittedDate)
                .ToListAsync();
        }

        public async Task<TblAssetApproval> CreateApprovalRequestAsync(int assetId, int userId, string metadataTag)
        {
            var extendedData = JsonSerializer.Serialize(new { metadataTag });

            var approval = new TblAssetApproval
            {
                AssetId = assetId,
                UploadedByUserId = userId,
                ApprovalStatus = (int)ApprovalStatus.Pending,
                SubmittedDate = DateTime.UtcNow,
                ExtendedData = extendedData
            };

            _dbContext.TblAssetApprovals.Add(approval);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Created approval request for asset {AssetId} by user {UserId} with tag {MetadataTag}",
                assetId, userId, metadataTag);

            return approval;
        }

        public async Task<bool> ApproveAssetAsync(int approvalId, int reviewerId, string? notes = null)
        {
            var approval = await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .FirstOrDefaultAsync(a => a.AssetApprovalId == approvalId);

            if (approval == null)
            {
                _logger.LogWarning("Approval record {ApprovalId} not found", approvalId);
                return false;
            }

            approval.ApprovalStatus = (int)ApprovalStatus.Approved;
            approval.ReviewedByUserId = reviewerId > 0 ? reviewerId : null;
            approval.ReviewedDate = DateTime.UtcNow;
            approval.ReviewNotes = notes;

            await _dbContext.SaveChangesAsync();

            // Update metadata file with new approval status
            if (approval.Asset != null)
            {
                await UpdateMetadataApprovalStatusAsync(approval.Asset.AssetPath, ApprovalStatus.Approved);
            }

            // A staged content replacement swaps in on approval: the superseded file
            // named in the replace marker is removed now that the replacement is live
            await _contentOverwrite.CompletePendingReplaceAsync(approval);

            InvalidateManifestForLocation(approval.Asset?.LocationId);

            _logger.LogInformation(
                "Asset {AssetId} approved by user {ReviewerId}",
                approval.AssetId, reviewerId);

            return true;
        }

        public async Task<bool> DeclineAssetAsync(int approvalId, int reviewerId, string? notes = null)
        {
            var approval = await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .FirstOrDefaultAsync(a => a.AssetApprovalId == approvalId);

            if (approval == null)
            {
                _logger.LogWarning("Approval record {ApprovalId} not found", approvalId);
                return false;
            }

            // Soft-decline: keep asset alive for re-submission window
            approval.ApprovalStatus = (int)ApprovalStatus.Declined;
            approval.ReviewedByUserId = reviewerId;
            approval.ReviewedDate = DateTime.UtcNow;
            approval.ReviewNotes = notes;
            approval.DeclinedDate = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Update metadata file so signage clients filter out declined content
            if (approval.Asset != null)
            {
                await UpdateMetadataApprovalStatusAsync(approval.Asset.AssetPath, ApprovalStatus.Declined);
            }

            InvalidateManifestForLocation(approval.Asset?.LocationId);

            _logger.LogInformation(
                "Asset {AssetId} declined by user {ReviewerId}. Reason: {Notes}",
                approval.AssetId, reviewerId, notes ?? "No reason provided");

            return true;
        }

        public async Task<bool> PurgeDeclinedAssetAsync(int assetApprovalId)
        {
            var approval = await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .FirstOrDefaultAsync(a => a.AssetApprovalId == assetApprovalId);

            if (approval == null) return false;

            var asset = approval.Asset;
            if (asset == null) return false;

            // Delete physical files (asset + metadata)
            var baseDirectory = _mediaPaths.GetRoot();
            if (!string.IsNullOrEmpty(baseDirectory) && !string.IsNullOrEmpty(asset.AssetPath))
            {
                var diskPath = Path.Combine(baseDirectory, asset.AssetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    if (File.Exists(diskPath))
                    {
                        File.Delete(diskPath);
                        _logger.LogInformation("Purged asset file: {DiskPath}", diskPath);
                    }

                    var metadataPath = diskPath + ".metadata";
                    if (File.Exists(metadataPath))
                    {
                        File.Delete(metadataPath);
                        _logger.LogInformation("Purged metadata file: {MetadataPath}", metadataPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete files for purged asset {AssetId}", asset.AssetId);
                }
            }

            // Delete database records
            _dbContext.TblAssetApprovals.Remove(approval);
            _dbContext.TblAssets.Remove(asset);
            await _dbContext.SaveChangesAsync();

            InvalidateManifestForLocation(asset.LocationId);

            _logger.LogInformation("Purged declined asset {AssetId} and approval record from database", asset.AssetId);

            return true;
        }

        public async Task<bool> ResubmitAsync(int assetApprovalId, int userId, string comment)
        {
            var approval = await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .FirstOrDefaultAsync(a => a.AssetApprovalId == assetApprovalId);

            if (approval == null || approval.ApprovalStatus != (int)ApprovalStatus.Declined)
            {
                _logger.LogWarning("Cannot resubmit approval {ApprovalId}: not found or not declined", assetApprovalId);
                return false;
            }

            // Reset to pending
            approval.ApprovalStatus = (int)ApprovalStatus.Pending;
            approval.ReviewedByUserId = null;
            approval.ReviewedDate = null;
            approval.DeclinedDate = null;

            await _dbContext.SaveChangesAsync();

            // Update metadata file
            if (approval.Asset != null)
            {
                await UpdateMetadataApprovalStatusAsync(approval.Asset.AssetPath, ApprovalStatus.Pending);
            }

            InvalidateManifestForLocation(approval.Asset?.LocationId);

            _logger.LogInformation("Asset approval {ApprovalId} resubmitted by user {UserId}", assetApprovalId, userId);

            return true;
        }

        /// <summary>
        /// Updates the ApprovalStatus field in the asset's metadata file.
        /// This allows signage clients to filter content based on approval status.
        /// </summary>
        private async Task UpdateMetadataApprovalStatusAsync(string? assetPath, ApprovalStatus status)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            try
            {
                var baseDirectory = _mediaPaths.GetRoot();
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    _logger.LogWarning("BaseRoot not configured, cannot update metadata file");
                    return;
                }

                // Convert web path to disk path (e.g., /MediaAssets/123/file.png -> BaseRoot/MediaAssets/123/file.png)
                var relativePath = assetPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
                var diskPath = Path.Combine(baseDirectory, relativePath);
                var metadataPath = diskPath + ".metadata";

                if (!File.Exists(metadataPath))
                {
                    _logger.LogWarning("Metadata file not found: {MetadataPath}", metadataPath);
                    return;
                }

                var doc = new XmlDocument();
                doc.Load(metadataPath);
                var nodes = doc.SelectNodes("usermetadata");

                if (nodes == null || nodes.Count == 0)
                    return;

                bool updated = false;
                foreach (XmlNode node in nodes)
                {
                    foreach (XmlNode line in node.ChildNodes)
                    {
                        if (line.Attributes?["name"]?.Value == "ApprovalStatus")
                        {
                            line.Attributes["value"].Value = ((int)status).ToString();
                            updated = true;
                            break;
                        }
                    }

                    // If ApprovalStatus field doesn't exist, add it
                    if (!updated)
                    {
                        var newElement = doc.CreateElement("field");
                        newElement.SetAttribute("name", "ApprovalStatus");
                        newElement.SetAttribute("datatype", "ovx_String");
                        newElement.SetAttribute("value", ((int)status).ToString());
                        node.AppendChild(newElement);
                    }
                }

                doc.Save(metadataPath);
                _logger.LogDebug("Updated metadata ApprovalStatus to {Status} for {AssetPath}", status, assetPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update metadata ApprovalStatus for {AssetPath}", assetPath);
            }
        }

        public async Task<TblAssetApproval?> GetAssetApprovalStatusAsync(int assetId)
        {
            return await _dbContext.TblAssetApprovals
                .Include(a => a.UploadedByUser)
                .Include(a => a.ReviewedByUser)
                .FirstOrDefaultAsync(a => a.AssetId == assetId);
        }

        public async Task<TblAssetApproval?> GetApprovalByIdAsync(int approvalId)
        {
            return await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .Include(a => a.UploadedByUser)
                .Include(a => a.ReviewedByUser)
                .FirstOrDefaultAsync(a => a.AssetApprovalId == approvalId);
        }

        public string? GetApprovalMetadataTag(TblAssetApproval approval)
        {
            if (string.IsNullOrEmpty(approval.ExtendedData))
                return null;

            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(approval.ExtendedData);
                // Check current key first, then fall back to legacy "hotelNumber" key for existing records
                if (data?.TryGetValue("metadataTag", out var tagElement) == true)
                    return tagElement.GetString();
                if (data?.TryGetValue("hotelNumber", out var legacyElement) == true)
                    return legacyElement.GetString();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse ExtendedData for approval {ApprovalId}", approval.AssetApprovalId);
            }

            return null;
        }

        public async Task<bool> IsAssetApprovedAsync(int assetId)
        {
            var approval = await _dbContext.TblAssetApprovals
                .FirstOrDefaultAsync(a => a.AssetId == assetId);

            // If no approval record exists, it's corporate content (always allowed)
            if (approval == null)
                return true;

            return approval.ApprovalStatus == (int)ApprovalStatus.Approved;
        }

        public async Task<List<int>> GetApprovedAssetIdsForTagAsync(string metadataTag)
        {
            var approvals = await _dbContext.TblAssetApprovals
                .Where(a => a.ApprovalStatus == (int)ApprovalStatus.Approved)
                .ToListAsync();

            return approvals
                .Where(a => GetApprovalMetadataTag(a) == metadataTag)
                .Select(a => a.AssetId)
                .ToList();
        }

        public async Task<List<TblAssetApproval>> GetApprovalsByLocationAsync(int locationId)
        {
            return await _dbContext.TblAssetApprovals
                .Include(a => a.Asset)
                .Include(a => a.UploadedByUser)
                .Include(a => a.ReviewedByUser)
                .Where(a => a.Asset != null && a.Asset.LocationId == locationId)
                .OrderByDescending(a => a.SubmittedDate)
                .ToListAsync();
        }

        public async Task<int> BulkApproveExistingAsync(int locationId, int reviewerId)
        {
            // Create approval records for all assets at this location that don't already have one
            var assetsWithoutApproval = await _dbContext.TblAssets
                .Where(a => a.LocationId == locationId && !a.IsDeleted
                    && !_dbContext.TblAssetApprovals.Any(ap => ap.AssetId == a.AssetId))
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var asset in assetsWithoutApproval)
            {
                _dbContext.TblAssetApprovals.Add(new TblAssetApproval
                {
                    AssetId = asset.AssetId,
                    ApprovalStatus = (int)ApprovalStatus.Approved,
                    UploadedByUserId = reviewerId,
                    ReviewedByUserId = reviewerId,
                    ReviewedDate = now,
                    SubmittedDate = now
                });
            }

            await _dbContext.SaveChangesAsync();

            InvalidateManifestForLocation(locationId);

            _logger.LogInformation("Bulk-approved {Count} assets for location {LocationId}", assetsWithoutApproval.Count, locationId);
            return assetsWithoutApproval.Count;
        }

        public async Task WithdrawPendingForAssetAsync(int assetId)
        {
            // Find all pending approval records for this asset
            var pendingApprovals = await _dbContext.TblAssetApprovals
                .Where(a => a.AssetId == assetId && a.ApprovalStatus == (int)ApprovalStatus.Pending)
                .ToListAsync();

            if (pendingApprovals.Count == 0)
                return;

            var approvalIds = pendingApprovals.Select(a => a.AssetApprovalId).ToList();

            // Withdraw all chain requests linked to these approvals
            var requests = await _dbContext.TblApprovalRequests
                .Where(r => approvalIds.Contains(r.AssetApprovalId) && r.Status == "pending")
                .ToListAsync();

            var requestIds = requests.Select(r => r.ApprovalRequestId).ToList();

            // Withdraw queue entries so they vanish from My Queue and All Pending
            if (requestIds.Count > 0)
            {
                var queueEntries = await _dbContext.TblApprovalQueueEntries
                    .Where(e => requestIds.Contains(e.ApprovalRequestId) && e.Status == "pending")
                    .ToListAsync();

                foreach (var entry in queueEntries)
                    entry.Status = "withdrawn";

                foreach (var request in requests)
                    request.Status = "withdrawn";
            }

            // Mark the asset approval itself as withdrawn so it disappears from My Submissions
            foreach (var approval in pendingApprovals)
                approval.ApprovalStatus = (int)ApprovalStatus.Withdrawn;

            await _dbContext.SaveChangesAsync();

            var locationId = await _dbContext.TblAssets
                .Where(a => a.AssetId == assetId)
                .Select(a => (int?)a.LocationId)
                .FirstOrDefaultAsync();
            InvalidateManifestForLocation(locationId);

            _logger.LogInformation("Withdrew {Count} pending approval record(s) for deleted asset {AssetId}", pendingApprovals.Count, assetId);
        }
    }
}
