namespace DXSuite.Director.Models;

/// <summary>
/// Configuration for a VideoWall location, stored in TblLocation.ExtendedData as JSON.
/// Wraps PanoramaConfiguration for display layout and adds the dual-scale context background.
/// </summary>
public class VideoWallConfiguration
{
    /// <summary>
    /// Display layout: placements, groups (counter rows), z-ordering, and spacing.
    /// PanoramaGroup.Role = "counter-row" marks airline-assignable rows.
    /// </summary>
    public PanoramaConfiguration Layout { get; set; } = new();

    /// <summary>
    /// Path to the physical-space context image (e.g., a rendering of the ticket counter desk).
    /// Rendered purely for visual orientation — not delivered to players.
    /// </summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>
    /// Scale factor applied to the context background image layer, independent of the content scale.
    /// Tuned so the background's on-screen width matches the content's
    /// (BackgroundImageWidth × BackgroundScaleFactor ≈ content width × ContentScaleFactor), allowing a
    /// background image of any native resolution to sit correctly behind the content.
    /// </summary>
    public double BackgroundScaleFactor { get; set; } = 0.15;

    /// <summary>
    /// Scale factor applied to the video wall content layer — counter rows, wall layers, and logo zones.
    /// These share one scale because they must stay pixel-aligned to render as a coherent wall.
    /// </summary>
    public double ContentScaleFactor { get; set; } = 0.4;

    /// <summary>
    /// Pixel width of one counter display at content scale. Used to calculate the background
    /// crop offset and width when rendering a cluster view.
    /// cropLeft = startIndex × CounterWidthPx × (BackgroundScaleFactor / ContentScaleFactor)
    /// </summary>
    public int CounterWidthPx { get; set; } = 1920;

    /// <summary>
    /// When true, this wall is driven by fixed-size player outputs (e.g. Moxie 4K)
    /// that wrap the canvas row into stacked strips. Enables per-output derived
    /// background renditions and the wrapped /videowall/layer URL scheme
    /// (wrap=OutputWidth x stripHeight, outh=OutputHeight). Strip height is the
    /// background row's display height from the layout; it is not stored here.
    /// </summary>
    public bool WrappedOutputsEnabled { get; set; }

    /// <summary>Pixel width of one physical player output (= wrap strip width).</summary>
    public int OutputWidth { get; set; } = 3840;

    /// <summary>Pixel height of one physical player output.</summary>
    public int OutputHeight { get; set; } = 2160;

    /// <summary>
    /// Vertical offset (canvas px) that admin surfaces add to every content element —
    /// counter rows, wall layers, logo zones, placements — so the content sits correctly
    /// against the context background image. Players never see it: on the wall,
    /// PositionTop values are relative to the content canvas itself.
    /// </summary>
    public double ContentTopOffsetPx { get; set; }

    /// <summary>
    /// Wall-scoped catalog of named logo roles (e.g. "Counter Logos", "Accent Logos").
    /// Logo zones reference a role via PanoramaGroup.LogoRoleKey, and the Airline
    /// Content uploader offers one upload slot per role present on the wall's logo
    /// zones. Airlines' per-role content lives in tblUserGroupLocationLogo; zones
    /// with no role key fall back to the legacy single LogoPath.
    /// </summary>
    public List<LogoRoleDefinition> LogoRoles { get; set; } = new();
}

/// <summary>A named logo role a wall's logo zones can be assigned to.</summary>
public class LogoRoleDefinition
{
    /// <summary>
    /// Stable slug identity referenced by PanoramaGroup.LogoRoleKey and
    /// tblUserGroupLocationLogo.LogoRoleKey; renaming the display Name must not
    /// change the Key or existing zone assignments and uploads would orphan.
    /// </summary>
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public int SortOrder { get; set; }
}
