using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Top-level manifest for a content package bundle. Serialized as manifest.json inside
/// the bundle ZIP. Carries all entity data plus metadata needed to reconstruct
/// records in a target environment.
/// </summary>
public class ContentPackageManifest
{
    /// <summary>
    /// Manifest schema version. Incremented when the shape of this DTO changes
    /// so importers can refuse or migrate older bundles.
    /// v2: VideoWall Location.ExtendedData display IDs are tokenized to display ContentPackageId
    ///     GUIDs (see WallLayoutIdRemapper), and UserGroupLocationDefaults are included. A pre-v2
    ///     importer would copy the tokenized ExtendedData verbatim and break the wall layout.
    /// v3: UserGroupLocationDefaults carry per-role Logos (tblUserGroupLocationLogo) and
    ///     side-by-side layer Segments (tblUserGroupLocationLayerContent). The importer only
    ///     reconciles these child rows for v3+ bundles so a v2 import cannot wipe target rows.
    /// v4: LocationMetadataRules, the two location targeting columns and asset ownership travel, and an
    ///     access rule carries ViewScope in place of MetadataFilterKey. Older bundles are translated on
    ///     read by <see cref="ContentPackageManifestUpgrade"/>, since every bundle in existence predates it.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// ISO-8601 timestamp when the bundle was generated.
    /// </summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>
    /// UserPrincipalName of the user who initiated the export.
    /// </summary>
    public string? ExportedByUser { get; set; }

    /// <summary>
    /// Friendly name of the source environment (customer-supplied, e.g. "Dev", "Pre-Prod").
    /// </summary>
    public string? SourceEnvironment { get; set; }

    /// <summary>
    /// Human-readable scope description ("Location: Airport Terminal A", "3 assets", etc.)
    /// </summary>
    public string? ScopeDescription { get; set; }

    public List<LocationGroupRecord> LocationGroups { get; set; } = new();
    public List<LocationRecord> Locations { get; set; } = new();
    public List<LocationMetadataRuleRecord> LocationMetadataRules { get; set; } = new();
    public List<DisplayGroupRecord> DisplayGroups { get; set; } = new();
    public List<DisplayRecord> Displays { get; set; } = new();
    public List<AssetGroupRecord> AssetGroups { get; set; } = new();
    public List<ConstraintRecord> Constraints { get; set; } = new();
    public List<AssetRecord> Assets { get; set; } = new();
    public List<AssetContentMetadataRecord> AssetContentMetadata { get; set; } = new();
    public List<ScheduleEventRecord> ScheduleEvents { get; set; } = new();
    public List<FloorPlanElementRecord> FloorPlanElements { get; set; } = new();
    public List<LocationAccessRuleRecord> LocationAccessRules { get; set; } = new();
    public List<AccessScheduleRecord> AccessSchedules { get; set; } = new();
    public List<ApprovalChainRecord> ApprovalChains { get; set; } = new();
    public List<UserGroupLocationDefaultRecord> UserGroupLocationDefaults { get; set; } = new();
}

/// <summary>
/// Base fields shared by every promotable record. ContentPackageId is the cross-environment
/// key; all other references between records use ContentPackageId (never int PKs).
/// </summary>
public abstract class ContentPackageRecord
{
    public Guid ContentPackageId { get; set; }
}

public class LocationGroupRecord : ContentPackageRecord
{
    public string? LocationGroupName { get; set; }
    public int SortOrder { get; set; }
}

public class LocationRecord : ContentPackageRecord
{
    public string? LocationName { get; set; }
    public int LocationTypeId { get; set; }
    public Guid LocationGroupContentPackageId { get; set; }
    public string? LocationImagePath { get; set; }
    public int SortOrder { get; set; }
    public string? BackgroundImagePath { get; set; }
    public double? DisplayAnchorTop { get; set; }
    public double? DisplayAnchorLeft { get; set; }
    public double? DisplayOffset { get; set; }
    public double? BackgroundImageHeight { get; set; }
    public double? BackgroundImageWidth { get; set; }
    public bool EnableShuffle { get; set; }
    public bool EnableFade { get; set; }
    public bool EnableContentApproval { get; set; }
    public bool TargetContentByMetadata { get; set; }

    /// <summary><c>all</c> or <c>any</c>. Absent from pre-v4 bundles, which read as <c>all</c>.</summary>
    public string? MetadataMatchMode { get; set; }

    public string? ExtendedData { get; set; }
}

/// <summary>
/// One key participating at a location. Matched on import by the location and the key name, not by
/// ContentPackageId, because whether a key participates at a location is the same fact in every environment.
/// </summary>
public class LocationMetadataRuleRecord : ContentPackageRecord
{
    public Guid LocationContentPackageId { get; set; }
    public string MetadataKey { get; set; } = null!;
    public string UntaggedContentPolicy { get; set; } = "all";
    public string UntaggedPlayerPolicy { get; set; } = "all";
}

public class DisplayGroupRecord : ContentPackageRecord
{
    public string? DisplayGroupName { get; set; }
    public int SortOrder { get; set; }
}

public class DisplayRecord : ContentPackageRecord
{
    public Guid LocationContentPackageId { get; set; }
    public string DisplayName { get; set; } = null!;
    public int DisplayMode { get; set; }
    public int DisplayTypeId { get; set; }
    public Guid DisplayGroupContentPackageId { get; set; }
    public double DisplayHeight { get; set; }
    public double DisplayWidth { get; set; }
    public Guid ConstraintContentPackageId { get; set; }
    public Guid? OverrideAssetContentPackageId { get; set; }
    public bool UseClock { get; set; }
    public double? ClockHeight { get; set; }
    public double? ClockWidth { get; set; }
    public bool UseVideoMask { get; set; }
    public bool EnableFade { get; set; }
    public bool EnableContentApproval { get; set; }
    public Guid? AssetLibraryLocationContentPackageId { get; set; }
    public string? StatusSelectorMetadataKey { get; set; }
    public int? EndpointId { get; set; }
    public int SortOrder { get; set; }
    public string? ExtendedData { get; set; }
}

public class AssetGroupRecord : ContentPackageRecord
{
    public string? AssetGroupName { get; set; }
}

public class ConstraintRecord : ContentPackageRecord
{
    public string? ConstraintName { get; set; }
    public Guid? OwningLocationContentPackageId { get; set; }
    public bool IsGlobal { get; set; }
    public int? ResolutionWidth { get; set; }
    public int? ResolutionHeight { get; set; }
    public string ResolutionMode { get; set; } = "aspect-ratio";
    public string? ExtendedData { get; set; }
}

public class AssetRecord : ContentPackageRecord
{
    public Guid LocationContentPackageId { get; set; }
    public string? AssetName { get; set; }
    /// <summary>
    /// Original relative asset path from the source environment.
    /// The importer rewrites this to match the target LocationId/ConstraintId.
    /// </summary>
    public string AssetPath { get; set; } = null!;
    public string? AssetInkPath { get; set; }
    public Guid AssetGroupContentPackageId { get; set; }
    public int AssetTypeId { get; set; }
    public double AssetHeight { get; set; }
    public double AssetWidth { get; set; }
    public Guid ConstraintContentPackageId { get; set; }
    public double Duration { get; set; }
    public string? VideoMaskPath { get; set; }
    public int? EndpointId { get; set; }
    public string? EndpointPath { get; set; }
    public string? WebRenderMode { get; set; }
    public bool EnableHealthMonitoring { get; set; }
    public string? ContentSourceUrl { get; set; }
    public string? ExtendedData { get; set; }
    public int SortOrder { get; set; }

    /// <summary>The owning user, resolved by name on import. Unresolved, the column is left empty.</summary>
    public string? UploadedByUserPrincipalName { get; set; }

    /// <summary>The owning group, resolved by name on import independently of the user.</summary>
    public string? UploadedByUserGroupName { get; set; }

    /// <summary>
    /// Original file name inside media/{ContentPackageId}/ — absent when the asset is
    /// a URL or external content with no local file.
    /// </summary>
    public string? MediaFileName { get; set; }
}

public class AssetContentMetadataRecord : ContentPackageRecord
{
    public Guid AssetContentPackageId { get; set; }
    public string MetadataKey { get; set; } = null!;
    public string MetadataValue { get; set; } = null!;
    public string? SetByUserPrincipalName { get; set; }
}

public class ScheduleEventRecord : ContentPackageRecord
{
    public int? ScheduleEventTypeId { get; set; }
    public Guid DisplayContentPackageId { get; set; }
    public Guid? LocationContentPackageId { get; set; }
    public string? AirlineCode { get; set; }
    /// <summary>
    /// Content assignment JSON. The asset ids inside Content[] are tokenized to asset
    /// ContentPackageId GUIDs on export and resolved back to target asset ids on import.
    /// </summary>
    public string ContentAssignment { get; set; } = null!;

    /// <summary>Owning airline user group (video-wall allocation), matched by name on import.</summary>
    public string? UserGroupName { get; set; }

    /// <summary>The block's default-content asset, resolved by ContentPackageId on import.</summary>
    public Guid? DefaultAssetContentPackageId { get; set; }

    /// <summary>Per-airline override constraint, resolved by ContentPackageId on import.</summary>
    public Guid? OverrideConstraintContentPackageId { get; set; }

    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool DefaultContent { get; set; }
    public bool Sunday { get; set; }
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
}

public class FloorPlanElementRecord : ContentPackageRecord
{
    public Guid LocationContentPackageId { get; set; }
    public Guid? ChildLocationContentPackageId { get; set; }
    public Guid? ChildDisplayContentPackageId { get; set; }
    public double PositionTop { get; set; }
    public double PositionLeft { get; set; }
    public double ElementWidth { get; set; }
    public double ElementHeight { get; set; }
    public string? IconPath { get; set; }
    public string? IconLabel { get; set; }
    public bool ShowLivePreview { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Position3DX { get; set; }
    public double? Position3DY { get; set; }
    public double? Position3DZ { get; set; }
    public int SortOrder { get; set; }
}

public class LocationAccessRuleRecord : ContentPackageRecord
{
    public Guid LocationContentPackageId { get; set; }
    /// <summary>
    /// UserPrincipalName of the rule's target user, when the rule is user-scoped.
    /// Resolved to a local int UserId on import.
    /// </summary>
    public string? UserPrincipalName { get; set; }
    /// <summary>
    /// Name of the user group, when the rule is group-scoped. Matched by name on import.
    /// </summary>
    public string? UserGroupName { get; set; }
    public string AccessLevel { get; set; } = "full";

    /// <summary>A view rule's scope, <c>all</c> or <c>none</c>. Null on every other access level.</summary>
    public string? ViewScope { get; set; }

    /// <summary>
    /// The pre-v4 field: a view rule's scope as a <c>__all__</c> / <c>__none__</c> sentinel, or a partial rule's
    /// metadata key. Read only, and translated into <see cref="ViewScope"/> when the bundle is read.
    /// </summary>
    [JsonPropertyName("MetadataFilterKey")]
    public string? LegacyMetadataFilterKey { get; set; }
}

public class AccessScheduleRecord : ContentPackageRecord
{
    public Guid LocationAccessRuleContentPackageId { get; set; }
    public Guid? DisplayContentPackageId { get; set; }
    public Guid? ConstraintContentPackageId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool Sunday { get; set; }
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public string? MetadataKey { get; set; }
}

/// <summary>
/// Per-airline, per-wall default content (background / mask / logo layers and the counter-content
/// constraint) from tblUserGroupLocationDefault. The entity has no ContentPackageId, so on import it
/// is matched by its natural key (target UserGroupId + target LocationId). The owning UserGroup and
/// the DefaultConstraint are resolved by name because user groups and global constraints are
/// environment-wide and managed outside content packages.
/// </summary>
public class UserGroupLocationDefaultRecord : ContentPackageRecord
{
    /// <summary>The video wall location this default belongs to.</summary>
    public Guid LocationContentPackageId { get; set; }

    /// <summary>Name of the owning airline user group, matched by name on import.</summary>
    public string? UserGroupName { get; set; }

    /// <summary>
    /// ContentPackageId of the counter-content constraint when it is location-scoped and included
    /// in the package. Null for global constraints (the common case) — see DefaultConstraintName.
    /// </summary>
    public Guid? DefaultConstraintContentPackageId { get; set; }

    /// <summary>
    /// Name of the counter-content constraint, used to resolve global constraints by name on import.
    /// </summary>
    public string? DefaultConstraintName { get; set; }

    public bool DefaultConstraintIsGlobal { get; set; }

    public string? BackgroundPath { get; set; }
    public string BackgroundMode { get; set; } = "fill-width";
    // Initializers keep bundles that predate these fields fully opaque on import.
    public int BackgroundOpacityPct { get; set; } = 100;
    public string? MaskPath { get; set; }
    public string MaskMode { get; set; } = "fill-width";
    public int MaskOpacityPct { get; set; } = 100;
    public string? LogoPath { get; set; }

    /// <summary>Per-role logo content (tblUserGroupLocationLogo). Empty for v2 bundles.</summary>
    public List<LogoRoleContentRecord> Logos { get; set; } = new();

    /// <summary>Ordered side-by-side layer segments (tblUserGroupLocationLayerContent). Empty for v2 bundles.</summary>
    public List<LayerSegmentRecord> Segments { get; set; } = new();
}

/// <summary>One per-role logo row; the role key references the wall config's LogoRoles catalog.</summary>
public class LogoRoleContentRecord
{
    public string LogoRoleKey { get; set; } = "";
    public string? ContentPath { get; set; }
    public string ContentMode { get; set; } = "fit";
}

/// <summary>One side-by-side layer segment ("background" or "mask"), ordered by SortOrder.</summary>
public class LayerSegmentRecord
{
    public string LayerRole { get; set; } = "background";
    public int SortOrder { get; set; }
    public string? ContentPath { get; set; }
    public int IntrinsicWidthPx { get; set; }
    public string ContentMode { get; set; } = "native";
}

public class ApprovalChainRecord : ContentPackageRecord
{
    public Guid LocationContentPackageId { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public Guid? LocationAccessRuleContentPackageId { get; set; }
    public bool AutoApproveMembers { get; set; }
    public bool EnableLlmAutoApproval { get; set; }
    public int LlmAutoApprovalUpToStageOrder { get; set; }
    public int DeclinedContentRetentionDays { get; set; }
}

/// <summary>
/// Selector for what the user wants to promote. Sent from the export UI to the
/// bundle builder. ContentPackageIds reference records currently in the source DB.
/// </summary>
public class ContentPackageSelection
{
    /// <summary>
    /// Locations to promote in full (all displays, assets, schedule events, etc. under each).
    /// </summary>
    public List<Guid> LocationContentPackageIds { get; set; } = new();

    /// <summary>
    /// Individual assets to promote. Each will include its content metadata automatically.
    /// Caller is responsible for ensuring parent Location exists in target or is included.
    /// </summary>
    public List<Guid> AssetContentPackageIds { get; set; } = new();

    /// <summary>
    /// Individual displays to promote. Associated schedule events are auto-included.
    /// </summary>
    public List<Guid> DisplayContentPackageIds { get; set; } = new();

    /// <summary>
    /// When true, optional admin-scoped entities (access rules, access schedules, approval
    /// chains) are included in the bundle. Off by default because these typically need
    /// careful per-environment review.
    /// </summary>
    public bool IncludeAccessControl { get; set; }

    /// <summary>
    /// When true, approval chain definitions are included. Implied by IncludeAccessControl
    /// but can be set independently.
    /// </summary>
    public bool IncludeApprovalChains { get; set; }

    /// <summary>
    /// Optional label describing the source environment, stored in the manifest.
    /// </summary>
    public string? SourceEnvironment { get; set; }

    /// <summary>
    /// Optional scope description shown in audit logs and import preview.
    /// </summary>
    public string? ScopeDescription { get; set; }
}
