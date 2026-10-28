namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// One Message waiting on one of the approver's groups in My Queue.
/// </summary>
/// <param name="Languages">The type's languages in its order: the columns the texts sit in side by side.</param>
/// <param name="Approved">The approved content screens show now; null for a new Message, which has none yet.</param>
/// <param name="Proposed">What approval would make the approved content: the pending revision, or the new Message.</param>
public sealed record MessageQueueItem(
    int QueueEntryId,
    int ApprovalRequestId,
    int MessageId,
    string TypeName,
    string GroupName,
    int StageNumber,
    int StageCount,
    DateTime SubmittedUtc,
    string? SubmittedBy,
    IReadOnlyList<MessageLanguage> Languages,
    MessageQueueVersion? Approved,
    MessageQueueVersion Proposed)
{
    public bool IsRevision => Approved is not null;
}

/// <param name="RevisionNumber">The approved revision the content is at; null for content not approved yet.</param>
/// <param name="Texts">Body by language code; a language with no text is missing.</param>
public sealed record MessageQueueVersion(
    string Name,
    int? RevisionNumber,
    IReadOnlyDictionary<string, string> Texts,
    IReadOnlyList<MessageQueueAsset> Assets);

/// <summary>The asset in one Asset Slot, such as the ASL video.</summary>
public sealed record MessageQueueAsset(string SlotKey, string? AssetName, string? AssetPath);

public enum MessageDecisionOutcome
{
    /// <summary>The last stage approved: the Message or its revision is the approved content now.</summary>
    Approved,

    /// <summary>The chain ended in a rejection: a revision is discarded, a new Message goes back to Draft.</summary>
    Rejected,

    /// <summary>The stage is approved and the next stage's approvers decide it now.</summary>
    Advanced,

    /// <summary>An AND stage recorded this group's approval and waits for its other groups.</summary>
    GroupRecorded,

    AlreadyDecided,

    /// <summary>The signed-in user is not a member of the entry's group.</summary>
    NotAllowed,

    Gone,
}

public sealed record MessageDecisionResult(MessageDecisionOutcome Outcome, string Message);
