namespace DXSuite.Shared.Authorization;

/// <summary>
/// A feature-level permission that can be granted to roles via the Roles settings tab.
/// DefaultRoles lists the built-in roles granted the permission when the key is first
/// discovered — chosen to preserve the pre-permission-system behavior exactly.
/// </summary>
public record FeaturePermissionDef(string Key, string Label, string Description, Roles.Role[] DefaultRoles);

/// <summary>
/// Permission key schemes for role-based access control. Tab keys are derived from the
/// module registry ("settings-tab:{key}", "config-tab:{key}"); feature and page keys are
/// fixed constants. The Admin role bypasses all permission checks in code.
/// </summary>
public static class PermissionKeys
{
    public const string SettingsTabPrefix = "settings-tab:";
    public const string ConfigTabPrefix = "config-tab:";

    /// <summary>
    /// The Roles settings tab itself is never grantable — only Admin may manage roles.
    /// </summary>
    public const string RolesSettingsTabKey = "roles";

    public static string ForSettingsTab(string tabKey) => SettingsTabPrefix + tabKey;
    public static string ForConfigTab(string tabKey) => ConfigTabPrefix + tabKey;

    public const string ContentUpload = "feature:content-upload";
    public const string ContentEdit = "feature:content-edit";
    public const string ContentDelete = "feature:content-delete";
    public const string ContentApprovalBypass = "feature:content-approval-bypass";
    public const string PageOverrides = "page:overrides";
    public const string PageSentinelAutomation = "page:sentinel-automation";
    public const string PageTicketCounterScheduler = "page:ticket-counter-scheduler";
    public const string PageDesign = "page:design";
    public const string PageContentLog = "page:content-log";
    public const string PageUserEventLog = "page:user-event-log";
    public const string ReportsExport = "feature:reports-export";

    // Publishing introduces new code to the fleet; deploying chooses who receives code that
    // already exists. One gate covered both, and a customer staging its own rollout should not
    // need the right to introduce a binary.
    public const string PlayerUpdatePublish = "feature:player-update-publish";
    public const string PlayerUpdateDeploy = "feature:player-update-deploy";

    // GTAA FSD 1.1 — Flight Data Editor (Aviation) and Messaging (Director).
    public const string PageFlightDataEditor = "page:flight-data-editor";
    public const string PageMessages = "page:messages";
    public const string PageMessagePlaybackLog = "page:message-playback-log";
    public const string MessagesAuthor = "feature:messages-author";
    public const string MessagesPriorityOverride = "feature:messages-priority-override";
    // These three are tab keys, not fixed constants — their permission is the module registry's own
    // ForConfigTab/ForSettingsTab derivation (EnumerateDefinedPermissions), already granted to the tab's
    // registered default. Named here only so a typo in the tab key used to check access fails at compile time;
    // never add them to FeaturePermissions below, or the sync finds the same key discovered twice.
    public const string ConfigTabMessageTypes = ConfigTabPrefix + "message-types";
    public const string ConfigTabZoneBuilder = ConfigTabPrefix + "zone-builder";
    public const string CoreApiConnectionConfigTabKey = "coreapi-connection";
    public const string ConfigTabCoreApiConnection = SettingsTabPrefix + CoreApiConnectionConfigTabKey;

    // Content defaults include NewUser because location access rules — not role — were
    // the only non-admin gate on playlist content before the permission system existed.
    public static readonly IReadOnlyList<FeaturePermissionDef> FeaturePermissions = new[]
    {
        new FeaturePermissionDef(ContentUpload, "Content Upload",
            "Upload content to playlists (still limited by the user's location access level).",
            new[] { Roles.Role.Architect, Roles.Role.Contributor, Roles.Role.NewUser }),
        new FeaturePermissionDef(ContentEdit, "Content Edit",
            "Edit playlist content (still limited by the user's location access level).",
            new[] { Roles.Role.Architect, Roles.Role.Contributor, Roles.Role.NewUser }),
        new FeaturePermissionDef(ContentDelete, "Content Delete",
            "Delete playlist content (still limited by the user's location access level).",
            new[] { Roles.Role.Architect, Roles.Role.Contributor, Roles.Role.NewUser }),
        new FeaturePermissionDef(ContentApprovalBypass, "Approval & Moderation Bypass",
            "See unapproved content and skip content moderation checks.",
            System.Array.Empty<Roles.Role>()),
        new FeaturePermissionDef(PageOverrides, "Overrides Pages",
            "Access the Override Control and Override Editor pages.",
            new[] { Roles.Role.Architect }),
        new FeaturePermissionDef(PageSentinelAutomation, "Sentinel Automation Page",
            "Access the Sentinel Automation page.",
            new[] { Roles.Role.Architect }),
        new FeaturePermissionDef(PageTicketCounterScheduler, "Counter Scheduler Page",
            "Access the Ticket Counter Scheduler page.",
            new[] { Roles.Role.Architect, Roles.Role.Contributor }),
        // Empty defaults = Admin-only until granted. Design exposes quoting and pricing
        // data, so unlike other keys it deliberately tightens the pre-permission behavior.
        new FeaturePermissionDef(PageDesign, "Design Pages",
            "Access the Design module pages (Designs, Catalog, AI Access).",
            System.Array.Empty<Roles.Role>()),
        new FeaturePermissionDef(PageContentLog, "Content Log Page",
            "Access the Content Log report of scheduled content history.",
            new[] { Roles.Role.Architect }),
        // Both Admin-only until granted, which is where the single existing gate already sat.
        new FeaturePermissionDef(PlayerUpdatePublish, "Player Update Publish",
            "Upload player artifacts and bless a known-good bootloader.",
            System.Array.Empty<Roles.Role>()),
        new FeaturePermissionDef(PlayerUpdateDeploy, "Player Update Deploy",
            "Create and run player update deployments from artifacts that already exist.",
            System.Array.Empty<Roles.Role>()),
        new FeaturePermissionDef(PageUserEventLog, "User Event Log Page",
            "Access the User Event Log audit trail.",
            new[] { Roles.Role.Architect }),
        new FeaturePermissionDef(ReportsExport, "Report Export",
            "Export a report (Excel or print) rather than only viewing it on screen.",
            new[] { Roles.Role.Architect, Roles.Role.Contributor }),
        new FeaturePermissionDef(PageFlightDataEditor, "Flight Data Editor Page",
            "Correct, add or hide flights when the RMS feed is wrong or down.",
            new[] { Roles.Role.Architect, Roles.Role.Contributor }),
        new FeaturePermissionDef(PageMessages, "Messages Page",
            "Send Emergency, IRROPS and Visual Paging messages to screens.",
            new[] { Roles.Role.Architect, Roles.Role.Contributor }),
        new FeaturePermissionDef(PageMessagePlaybackLog, "Message Playback Log Page",
            "View the history of messages sent to screens.",
            new[] { Roles.Role.Architect, Roles.Role.Contributor }),
        new FeaturePermissionDef(MessagesAuthor, "Author Canned Messages",
            "Write and submit canned Messages for approval.",
            new[] { Roles.Role.Architect, Roles.Role.Contributor }),
        new FeaturePermissionDef(MessagesPriorityOverride, "Message Priority Override",
            "Override a Message Type's normal priority rank on an individual send.",
            new[] { Roles.Role.Architect }),
    };
}
