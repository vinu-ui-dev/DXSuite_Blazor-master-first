namespace DXSuite.Shared.Audit;

/// <summary>Action keys the User Event Log records, grouped by the capability that writes them. A new action
/// belongs here, never as a string literal at the call site, so every actor/target/detail shape stays documented
/// in one place.</summary>
public static class UserEventActionKeys
{
    // CoreAPI watcher (FSD 1.1 CAPI-05)
    public const string CoreApiReachable = "coreapi.reachable";
    public const string CoreApiUnreachable = "coreapi.unreachable";
    public const string CoreApiFeedUp = "coreapi.feed-up";
    public const string CoreApiFeedDown = "coreapi.feed-down";

    // Flight Data Editor (FDE)
    public const string FdeFlightAdded = "fde.flight-added";
    public const string FdeFlightEdited = "fde.flight-edited";
    public const string FdeRollChanged = "fde.roll-changed";
    public const string FdeEditPreserved = "fde.edit-preserved";
    public const string FdeEditCleared = "fde.edit-cleared";
    public const string FdeReviewUseFeed = "fde.review-use-feed";
    public const string FdeReviewPreserveAll = "fde.review-preserve-all";
    public const string FdeOutageSwitched = "fde.outage-switched";
    public const string FdeSettingsChanged = "fde.settings-changed";

    // Business Rules (shared by Messaging and the Flight Data Editor)
    public const string BusinessRuleChanged = "business-rule.changed";

    // Messages and Activations (MSG-xx)
    public const string MessageCreated = "message.created";
    public const string MessageEdited = "message.edited";
    public const string MessageDeleted = "message.deleted";
    public const string MessageRetired = "message.retired";
    public const string MessageRevisionSubmitted = "message.revision-submitted";
    public const string MessageApproved = "message.approved";
    public const string MessageRejected = "message.rejected";
    public const string ActivationSent = "activation.sent";
    public const string ActivationCancelled = "activation.cancelled";
    public const string ActivationEnded = "activation.ended";
    public const string ActivationPriorityOverrideSet = "activation.priority-override-set";

    // Message Types and their access/approval configuration (section 3.10, ZONE-07 to ZONE-09)
    public const string MessageTypeCreated = "message-type.created";
    public const string MessageTypeEdited = "message-type.edited";
    public const string MessageTypeDeleted = "message-type.deleted";
    public const string MessageTypeTopPriorityMoved = "message-type.top-priority-moved";
    public const string MessageAccessRuleAdded = "message-access-rule.added";
    public const string MessageAccessRuleRemoved = "message-access-rule.removed";
    public const string ApprovalChainChanged = "approval-chain.changed";

    // Display Zones (ZONE-01 to ZONE-14)
    public const string DisplayZoneCreated = "display-zone.created";
    public const string DisplayZoneRenamed = "display-zone.renamed";
    public const string DisplayZoneMembershipChanged = "display-zone.membership-changed";
    public const string DisplayZoneRetired = "display-zone.retired";
    public const string DisplayZoneDeleted = "display-zone.deleted";

    // Reports (shared across every report, AUD-14/AUD-15)
    public const string ReportExported = "report.exported";
    public const string ReportPrinted = "report.printed";
}
