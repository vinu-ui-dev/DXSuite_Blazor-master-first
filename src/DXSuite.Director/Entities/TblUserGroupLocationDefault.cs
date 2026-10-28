using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Per-airline, per-wall default content assignments.
/// Drives cluster content resolution when a schedule event has no per-event override.
/// One row per (UserGroup, Location, StateName) — a "state" is a named content variant
/// for dayparting; exactly one state per pair is the default fallback.
/// </summary>
[Table("tblUserGroupLocationDefault")]
[Index("UserGroupId", "LocationId", "StateName", IsUnique = true, Name = "UX_tblUserGroupLocationDefault_GroupLocationState")]
public class TblUserGroupLocationDefault
{
    [Key]
    public int UserGroupLocationDefaultId { get; set; }

    public int UserGroupId { get; set; }

    public int LocationId { get; set; }

    /// <summary>Named content state for dayparting. Legacy single-state rows are "Default".</summary>
    [StringLength(50)]
    public string StateName { get; set; } = "Default";

    /// <summary>
    /// The fallback state played when no sibling state's schedule matches the current time.
    /// Exactly one row per (UserGroup, Location) carries this flag.
    /// </summary>
    public bool IsDefaultState { get; set; } = true;

    /// <summary>
    /// JSON recurrence window (AirlineStateSchedule: start/end time + days of week,
    /// wrap-aware like schedule events) selecting when this state is active.
    /// Null on the default state — it plays whenever nothing else matches.
    /// </summary>
    public string? StateScheduleJson { get; set; }

    /// <summary>Constraint (asset library filter) for counter display content. Null = fall back to location global.</summary>
    public int? DefaultConstraintId { get; set; }

    /// <summary>
    /// URL or uploaded-file path for the background layer asset.
    /// http(s):// URLs are web/SVG content that scales to cluster width.
    /// Relative paths (e.g. /MediaAssets/...) are uploaded image or video files.
    /// </summary>
    [StringLength(500)]
    public string? BackgroundPath { get; set; }

    /// <summary>
    /// Render mode for the background layer (see LayerRenderMode for semantics).
    /// Valid values: "fill-width" | "fill-height" | "fit" | "tile" | "native"
    /// Only meaningful when BackgroundPath is an image or video (not a web URL).
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string BackgroundMode { get; set; } = "fill-width";

    /// <summary>
    /// Layer opacity 0–100 applied to whatever the background layer renders
    /// (media, iframe, or segment sequence) so layers beneath show through.
    /// </summary>
    public int BackgroundOpacityPct { get; set; } = 100;

    /// <summary>
    /// URL or uploaded-file path for the mask layer asset.
    /// </summary>
    [StringLength(500)]
    public string? MaskPath { get; set; }

    /// <summary>
    /// Render mode for the mask layer (see LayerRenderMode for semantics).
    /// Valid values match BackgroundMode. Only meaningful for image/video mask assets.
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string MaskMode { get; set; } = "fill-width";

    /// <summary>Layer opacity 0–100 for the mask layer; see BackgroundOpacityPct.</summary>
    public int MaskOpacityPct { get; set; } = 100;

    /// <summary>
    /// Path to the airline's logo asset shown in logo zones on the video wall canvas.
    /// Supports uploaded image/video files (relative /MediaAssets/... path) and HTML files
    /// served via /videowall/html-asset. Null means no logo is configured for this airline.
    /// </summary>
    [StringLength(500)]
    public string? LogoPath { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime LastEditDate { get; set; }

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;

    [ForeignKey("LocationId")]
    public virtual TblLocation Location { get; set; } = null!;

    [ForeignKey("DefaultConstraintId")]
    public virtual TblConstraint? DefaultConstraint { get; set; }

    /// <summary>Per-role logo content; empty for airlines still on the single LogoPath.</summary>
    public virtual ICollection<TblUserGroupLocationLogo> Logos { get; set; } = new List<TblUserGroupLocationLogo>();

    /// <summary>Ordered side-by-side layer segments; empty for airlines on single-file layers.</summary>
    public virtual ICollection<TblUserGroupLocationLayerContent> LayerContents { get; set; } = new List<TblUserGroupLocationLayerContent>();
}
