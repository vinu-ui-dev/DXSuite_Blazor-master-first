namespace DXSuite.Director.Services;

/// <summary>
/// Input DTO for defining a stage in a chain configuration.
/// </summary>
public class StageDefinition
{
    public int StageOrder { get; set; }
    public string? StageName { get; set; }
    public string IntraStageOperator { get; set; } = "OR";
    public List<int> GroupIds { get; set; } = new();
}

/// <summary>
/// Result of an approve/decline action.
/// </summary>
public class ApprovalActionResult
{
    public bool Success { get; set; }

    /// <summary>
    /// "group-approved" (AND stage, waiting for others),
    /// "stage-complete" (stage done, advancing),
    /// "chain-approved" (entire chain complete),
    /// "chain-declined",
    /// "already-resolved" (concurrency: someone else acted first),
    /// "error"
    /// </summary>
    public string Outcome { get; set; } = null!;
    public string? Message { get; set; }
}

/// <summary>
/// View model for a single item in a user's approval queue.
/// </summary>
public class ApprovalQueueItem
{
    public int QueueEntryId { get; set; }
    public int ApprovalRequestId { get; set; }
    public int AssetApprovalId { get; set; }
    public int AssetId { get; set; }
    public string? AssetName { get; set; }
    public string? AssetPath { get; set; }
    public string? LocationName { get; set; }
    public string? UploadedByName { get; set; }
    public DateTime SubmittedDate { get; set; }
    public int GroupId { get; set; }
    public string? GroupName { get; set; }
    public int CurrentStageOrder { get; set; }
    public int TotalStages { get; set; }
    public string? ExtendedData { get; set; }
}

/// <summary>
/// Detailed view of an approval request with full stage progression history.
/// </summary>
public class ApprovalRequestDetail
{
    public int ApprovalRequestId { get; set; }
    public int AssetApprovalId { get; set; }
    public string Status { get; set; } = null!;
    public DateTime SubmittedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? UploadedByName { get; set; }
    public string? AssetName { get; set; }
    public string? LocationName { get; set; }
    public List<StageDetail> Stages { get; set; } = new();
}

public class StageDetail
{
    public int StageOrder { get; set; }
    public string? StageName { get; set; }
    public string IntraStageOperator { get; set; } = null!;
    public bool IsCurrentStage { get; set; }
    public List<GroupActionDetail> Groups { get; set; } = new();
}

public class GroupActionDetail
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = null!;
    public string QueueStatus { get; set; } = null!;
    public string? ActingUserName { get; set; }
    public string? ActionType { get; set; }
    public string? Notes { get; set; }
    public DateTime? ActionDate { get; set; }
}

/// <summary>
/// Result of chain definition validation.
/// </summary>
public class ChainValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result from ApprovalActionModal when user confirms an approve/decline.
/// </summary>
public class ApprovalActionModalResult
{
    public int QueueEntryId { get; set; }
    public int UserId { get; set; }
    public int GroupId { get; set; }
    public int AssetApprovalId { get; set; }
    public int? ApprovalRequestId { get; set; }
    public string? Notes { get; set; }
    public string ActionType { get; set; } = null!;
}

/// <summary>
/// View model for a comment in the approval thread.
/// </summary>
public class ApprovalCommentDto
{
    public int ApprovalCommentId { get; set; }
    public string UserName { get; set; } = null!;
    public string CommentText { get; set; } = null!;
    public string CommentType { get; set; } = null!;
    public DateTime CreatedDate { get; set; }
}


/// <summary>
/// View model for an item in the "My Submissions" tab.
/// </summary>
public class SubmissionItem
{
    public int AssetApprovalId { get; set; }
    public int AssetId { get; set; }
    public string? AssetName { get; set; }
    public string? AssetPath { get; set; }
    public string? LocationName { get; set; }
    public int ApprovalStatus { get; set; }
    public DateTime SubmittedDate { get; set; }
    public DateTime? ReviewedDate { get; set; }
    public DateTime? DeclinedDate { get; set; }
    public int? DeclinedContentRetentionDays { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// True when content was approved without a human reviewer (LLM auto-approval or chain auto-approval).
    /// </summary>
    public bool IsAutoApproved => ApprovalStatus == 1 && ReviewedByUserId == null;
}
