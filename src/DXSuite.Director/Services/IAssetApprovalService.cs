using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// Service for managing content approval workflow
    /// </summary>
    public interface IAssetApprovalService
    {
        /// <summary>
        /// Get all pending approval requests ordered by submission date
        /// </summary>
        Task<List<TblAssetApproval>> GetPendingApprovalsAsync();

        /// <summary>
        /// Get all approvals with a specific status
        /// </summary>
        Task<List<TblAssetApproval>> GetApprovalsByStatusAsync(ApprovalStatus status);

        /// <summary>
        /// Create an approval request for a user-uploaded asset
        /// </summary>
        /// <param name="assetId">The uploaded asset ID</param>
        /// <param name="userId">The user who uploaded the asset</param>
        /// <param name="metadataTag">The metadata tag value identifying the content grouping (e.g. room number, department)</param>
        Task<TblAssetApproval> CreateApprovalRequestAsync(int assetId, int userId, string metadataTag);

        /// <summary>
        /// Approve an asset for playback on devices
        /// </summary>
        /// <param name="approvalId">The approval record ID</param>
        /// <param name="reviewerId">The admin user approving the content</param>
        /// <param name="notes">Optional reviewer notes</param>
        Task<bool> ApproveAssetAsync(int approvalId, int reviewerId, string? notes = null);

        /// <summary>
        /// Decline an asset, preventing playback on devices
        /// </summary>
        /// <param name="approvalId">The approval record ID</param>
        /// <param name="reviewerId">The admin user declining the content</param>
        /// <param name="notes">Optional reason for declining</param>
        Task<bool> DeclineAssetAsync(int approvalId, int reviewerId, string? notes = null);

        /// <summary>
        /// Get the approval status for a specific asset
        /// </summary>
        /// <param name="assetId">The asset ID to check</param>
        Task<TblAssetApproval?> GetAssetApprovalStatusAsync(int assetId);

        /// <summary>
        /// Get the approval record by ID
        /// </summary>
        Task<TblAssetApproval?> GetApprovalByIdAsync(int approvalId);

        /// <summary>
        /// Extract the metadata tag value from an approval's ExtendedData JSON
        /// </summary>
        string? GetApprovalMetadataTag(TblAssetApproval approval);

        /// <summary>
        /// Check if an asset is approved for playback
        /// </summary>
        Task<bool> IsAssetApprovedAsync(int assetId);

        /// <summary>
        /// Permanently delete a declined asset's files and DB records.
        /// Used by DeclinedContentCleanupService after retention window expires.
        /// </summary>
        Task<bool> PurgeDeclinedAssetAsync(int assetApprovalId);

        /// <summary>
        /// Re-submit a declined asset for approval. Resets status to Pending and clears review fields.
        /// </summary>
        Task<bool> ResubmitAsync(int assetApprovalId, int userId, string comment);

        /// <summary>
        /// Get all approved asset IDs matching a specific metadata tag value
        /// </summary>
        Task<List<int>> GetApprovedAssetIdsForTagAsync(string metadataTag);

        /// <summary>
        /// Get all approval records for assets belonging to a specific location, ordered by submission date descending.
        /// Used by ScheduleEditor to surface pending Create/Template media for approval.
        /// </summary>
        Task<List<TblAssetApproval>> GetApprovalsByLocationAsync(int locationId);

        /// <summary>
        /// Create approval records for all existing unapproved assets at a location, marking them approved.
        /// Used when an approval chain is first enabled to grandfather in pre-existing content.
        /// </summary>
        Task<int> BulkApproveExistingAsync(int locationId, int reviewerId);

        /// <summary>
        /// Withdraw all pending approval records for a deleted asset so it no longer appears
        /// in any approval queue or submissions list.
        /// </summary>
        Task WithdrawPendingForAssetAsync(int assetId);
    }
}
