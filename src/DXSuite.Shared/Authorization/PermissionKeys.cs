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

    // Publishing introduces new code to the fleet; deploying chooses who receives code that
    // already exists. One gate covered both, and a customer staging its own rollout should not
    // need the right to introduce a binary.
    public const string PlayerUpdatePublish = "feature:player-update-publish";
    public const string PlayerUpdateDeploy = "feature:player-update-deploy";

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
    };
}
