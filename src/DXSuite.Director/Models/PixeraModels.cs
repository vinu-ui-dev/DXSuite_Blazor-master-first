namespace DXSuite.Director.Models;

// ─── Wall-assignments API response ────────────────────────────────────────────

/// <summary>
/// A single content asset returned by the wall-assignments endpoint.
/// Mask sibling assets (named {basename}_mask.{ext}) are folded into the primary
/// asset entry rather than listed separately — the consumer sees one item per
/// piece of content, with an optional accompanying mask path.
/// </summary>
public record WallAssignmentAsset(
    int     AssetId,
    string? AssetName,
    string? AssetPath,
    /// <summary>
    /// Path of the paired _mask asset if one exists in the same content pool,
    /// or the asset's VideoMaskPath field as a fallback. Null when no mask is present.
    /// </summary>
    string? MaskAssetPath,
    int     AssetTypeId,
    double  Duration,
    int     SortOrder);

/// <summary>
/// One cluster (contiguous run of counter displays) in the wall-assignments response.
/// Clusters with no active airline have GroupId=null and an empty Assets list.
/// </summary>
public record WallAssignmentCluster(
    int?   GroupId,
    string GroupName,
    IReadOnlyList<string> DisplayNames,
    double LeftPx,
    double WidthPx,
    int?   TransientLocationId,
    string? MaskPath,
    string? BackgroundPath,
    IReadOnlyList<WallAssignmentAsset> Assets);

/// <summary>
/// One individual counter display in the wall-assignments response.
/// Each display in the wall's AllowAirlineAllocation groups appears here regardless
/// of whether it has active content — the consumer always gets the full 36-display picture.
/// Assets are the full resolved pool shared across the display's airline cluster.
/// ActiveAsset is the specific asset currently selected/playing on this display, determined
/// by OverrideAssetId → active event DefaultAssetId/ContentAssignment → DefaultContent event.
/// </summary>
public record WallAssignmentDisplay(
    int    DisplayId,
    string DisplayName,
    /// <summary>Left edge of this individual display in canvas pixels.</summary>
    double LeftPx,
    /// <summary>Width of this individual display in canvas pixels (scaled).</summary>
    double WidthPx,
    int?   GroupId,
    string GroupName,
    int?   TransientLocationId,
    /// <summary>
    /// The asset currently selected/playing on this display.
    /// Null when no status has been selected or no airline is allocated to this display.
    /// </summary>
    WallAssignmentAsset? ActiveAsset,
    IReadOnlyList<WallAssignmentAsset> Assets);

/// <summary>
/// Top-level response for GET /api/videowall/wall-assignments.
/// Clusters groups consecutive same-airline displays; Displays expands them
/// into one entry per physical counter display.
/// </summary>
public record WallAssignmentsResponse(
    int      LocationId,
    string   LocationName,
    DateTime AsOf,
    IReadOnlyList<WallAssignmentCluster> Clusters,
    IReadOnlyList<WallAssignmentDisplay> Displays);

// ──────────────────────────────────────────────────────────────────────────────

public record DisplayCluster(
    int?   GroupId,
    string GroupName,
    int    StartIndex,
    int    Count,
    string? MaskPath,
    string? MaskMode,
    string? BackgroundPath,
    string? BackgroundMode,
    IReadOnlyList<string> DisplayNames,
    /// <summary>
    /// LocationId of the transient cluster child location owned by this cluster's airline group.
    /// Null when no airline is active or no transient location exists for the group.
    /// Used by the Pixera counter layer to load content directly from the airline's schedule.
    /// </summary>
    int? TransientLocationId = null,
    /// <summary>Pixel left offset of the cluster's first display in layout coordinates (scaled by DisplayScale).</summary>
    double LeftPx  = 0,
    /// <summary>Pixel width spanning all displays + all gaps (internal and trailing). The trailing gap stretches the cluster to where the next cluster begins, so mask/background iframes cover the full inter-airline space with no uncovered strip.</summary>
    double WidthPx = 0,
    /// <summary>
    /// Asset pinned as the counter content for this cluster's active schedule block.
    /// Null = no pinned default; counter-content falls through to the constraint library.
    /// </summary>
    int? DefaultAssetId = null,
    /// <summary>
    /// Unix timestamp (seconds) of the mask file's last modification on disk.
    /// Included in the shell's sig() so a re-upload of the same filename forces a fresh iframe load.
    /// Zero when the mask path is null or the file cannot be stat'd.
    /// </summary>
    long MaskVersion = 0,
    /// <summary>
    /// Unix timestamp (seconds) of the background file's last modification on disk.
    /// Same versioning purpose as MaskVersion.
    /// </summary>
    long BackgroundVersion = 0,
    /// <summary>
    /// True when this cluster contains the last display on the wall (rightmost by canvas X).
    /// Used by the mask/background layer shell to extend the cluster's region div to the
    /// viewport right edge (right: 0) rather than stopping at the calculated widthPx, so the
    /// mask fills any trailing canvas space beyond the last physical counter display.
    /// </summary>
    bool IsRightmost = false,
    /// <summary>
    /// Ordered side-by-side mask segments (tblUserGroupLocationLayerContent). When present the
    /// shell renders these instead of MaskPath: each at native width, wrapping back to the first
    /// until the cluster is covered.
    /// </summary>
    IReadOnlyList<LayerContentSegment>? MaskSegments = null,
    /// <summary>Ordered side-by-side background segments; same semantics as MaskSegments.</summary>
    IReadOnlyList<LayerContentSegment>? BackgroundSegments = null,
    /// <summary>Layer opacity 0–100 applied to whatever the mask layer renders.</summary>
    int MaskOpacityPct = 100,
    /// <summary>Layer opacity 0–100 applied to whatever the background layer renders.</summary>
    int BackgroundOpacityPct = 100);

/// <summary>
/// One side-by-side segment of a cluster layer. WidthPx is the native pixel width
/// (ffprobe-stamped at upload); zero means unknown — the segment fills the remaining
/// cluster width and ends the sequence.
/// </summary>
public record LayerContentSegment(string Path, long Version, double WidthPx);

/// <summary>
/// A single logo zone on the video wall canvas. The zone is positioned explicitly in the
/// layout editor; the logo asset is resolved at runtime by finding which counter cluster's
/// X range contains this zone's midpoint. Multiple zones may resolve to the same airline
/// when a contiguous allocation spans more than one logo zone position.
/// </summary>
public record LogoZone(
    /// <summary>Left edge of the zone in canvas pixels.</summary>
    double LeftPx,
    /// <summary>Top edge of the zone in canvas pixels.</summary>
    double TopPx,
    /// <summary>Width of the zone in canvas pixels.</summary>
    double WidthPx,
    /// <summary>Height of the zone in canvas pixels.</summary>
    double HeightPx,
    int    ZIndex,
    /// <summary>GroupId of the airline whose allocation covers this zone's X midpoint. Null when unallocated.</summary>
    int?   GroupId,
    /// <summary>
    /// Resolved content path: the airline's per-role upload when the zone carries a
    /// LogoRoleKey, else the legacy TblUserGroupLocationDefault.LogoPath. Null when
    /// unallocated or the airline has nothing for this zone's role.
    /// </summary>
    string? LogoPath,
    /// <summary>Unix timestamp of the logo file's last modification for CEF cache-busting. Zero when no file exists.</summary>
    long   LogoVersion,
    /// <summary>Key of the wall logo role this zone renders; null = legacy default role.</summary>
    string? RoleKey = null,
    /// <summary>Render mode for image/video content (see LayerRenderMode). Legacy zones render "fit".</summary>
    string ContentMode = "fit");

/// <summary>
/// A wall-level background element resolved from a layout group with Role="background".
/// Each display in a background group is one zone; its content is whatever asset is
/// assigned to that display (override → active timed event → default-content event),
/// the same resolution the admin wall preview uses. The background layer shell renders
/// zones beneath the per-airline cluster backgrounds, so airline backgrounds override
/// the wall background wherever they are assigned.
/// </summary>
public record BackgroundZone(
    /// <summary>Left edge of the zone in canvas pixels.</summary>
    double LeftPx,
    /// <summary>Width of one tile in canvas pixels. With LoopCount > 1, DisplayWidth stores the per-tile width.</summary>
    double WidthPx,
    int    ZIndex,
    /// <summary>Times the content repeats side-by-side across the canvas (PanoramaGroup.HorizontalImageLoop, min 1).</summary>
    int    LoopCount,
    /// <summary>Resolved asset path for the display's assigned content. Null when nothing is assigned.</summary>
    string? ContentPath,
    /// <summary>Unix timestamp of the content file's last modification for CEF cache-busting. Zero when no file exists.</summary>
    long   ContentVersion,
    /// <summary>
    /// True when ContentPath is a derived per-output rendition with the wrap strips
    /// baked into the file. The shell renders it once, full output, without slicing.
    /// </summary>
    bool   Prewrapped = false);

public record AllocationResult(
    int LocationId,
    int TotalDisplays,
    IReadOnlyList<DisplayCluster> Clusters,
    IReadOnlyList<LogoZone>? LogoZones = null,
    IReadOnlyList<BackgroundZone>? BackgroundZones = null);
