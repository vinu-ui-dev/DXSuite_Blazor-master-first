using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public interface IApprovalChainService
{
    // Chain configuration
    Task<TblApprovalChain?> GetChainForLocationAsync(int locationId);
    Task<TblApprovalChain?> GetChainForAccessRuleAsync(int locationAccessRuleId);
    Task CreateOrUpdateChainAsync(int locationId, bool isEnabled, bool enableLlmAutoApproval, int llmAutoApprovalUpToStageOrder, bool autoApproveMembers, List<StageDefinition> stages, string editUser, int declinedContentRetentionDays = 3);
    Task DeleteChainAsync(int locationId);
    ChainValidationResult ValidateChainDefinition(List<StageDefinition> stages);

    // Chain execution
    /// <summary>
    /// Submits an asset to the chain its uploader's identity routes to: the rule picked at upload, else the
    /// asset's owning group re-resolved to a current rule, else the location's default chain.
    /// </summary>
    /// <param name="pickedRuleId">The rule behind a fresh "Uploading as" pick. Null on resubmit and on paths with no pick.</param>
    Task<TblApprovalRequest?> SubmitToChainAsync(int assetApprovalId, int locationId, bool passedLlmModeration = false, int? uploadingUserId = null, int? pickedRuleId = null);
    Task<ApprovalActionResult> ApproveAsync(int queueEntryId, int actingUserId, int actingGroupId, string? notes = null);
    Task<ApprovalActionResult> DeclineAsync(int queueEntryId, int actingUserId, int actingGroupId, string? notes = null);

    // Query
    Task<List<ApprovalQueueItem>> GetPendingItemsForUserAsync(int userId);
    Task<List<ApprovalQueueItem>> GetAllPendingItemsAsync();
    Task<ApprovalRequestDetail?> GetRequestDetailAsync(int approvalRequestId);

    // Comments
    Task AddCommentAsync(int assetApprovalId, int? approvalRequestId, int userId, string commentText, string commentType);
    Task<List<ApprovalCommentDto>> GetCommentsAsync(int assetApprovalId);

    // Submissions
    Task<List<SubmissionItem>> GetSubmissionsForUserAsync(int userId);
}
