namespace DXSuite.Director.Models;

/// <summary>
/// Configuration for a Panorama location, stored in TblLocation.ExtendedData as JSON.
/// Panorama locations render a display wall with positioned screens on a background image.
/// </summary>
public class PanoramaConfiguration
{
    public double ScaleFactor { get; set; } = 0.4;
    public List<PanoramaPlacement> Placements { get; set; } = new();
    public List<PanoramaGroup> Groups { get; set; } = new();
}

/// <summary>
/// An individual display placement within a Panorama layout
/// </summary>
public class PanoramaPlacement
{
    public int DisplayId { get; set; }
    public double PositionTop { get; set; }
    public double PositionLeft { get; set; }
    public int ZIndex { get; set; }
    public double DisplayScale { get; set; } = 1.0;

    /// <summary>
    /// Layer role within a VideoWall location: "counter", "background", or "mask".
    /// Null for standard Panorama placements.
    /// </summary>
    public string? Role { get; set; }
}

/// <summary>
/// A group of displays arranged in a row or column within a Panorama layout
/// </summary>
public class PanoramaGroup
{
    public string GroupName { get; set; } = "";
    public List<int> DisplayIds { get; set; } = new();
    public double PositionTop { get; set; }
    public double PositionLeft { get; set; }
    public double Spacing { get; set; }

    /// <summary>
    /// Cyclic gap pattern between consecutive displays in the group.
    /// Gaps[i] is the pixel gap inserted after display i (before display i+1), cycling modulo Gaps.Count.
    /// When empty, falls back to the legacy <see cref="Spacing"/> value for backward compatibility.
    /// </summary>
    public List<double> Gaps { get; set; } = new();

    public string Direction { get; set; } = "horizontal";
    public int ZIndex { get; set; }
    public double DisplayScale { get; set; } = 1.0;

    /// <summary>
    /// Group role within a VideoWall location: "counter-row" marks this as an airline-assignable
    /// row of counter displays. Null for standard Panorama groups.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// When true, airline groups may be scheduled onto the individual displays in this group
    /// via the Ticket Counter Scheduler.
    /// </summary>
    public bool AllowAirlineAllocation { get; set; }

    /// <summary>
    /// For Role="logo" groups: key of the wall's logo role this zone renders
    /// (see VideoWallConfiguration.LogoRoles). Null means the wall's implicit
    /// default role, which resolves to the airline's legacy single LogoPath —
    /// existing walls therefore render unchanged without any data migration.
    /// </summary>
    public string? LogoRoleKey { get; set; }

    /// <summary>
    /// For counter rows: optional coverage bound (canvas px) pinning the left edge
    /// of whichever airline cluster contains this row's first counter, superseding
    /// the derived rules (extend-to-x=0 / boundary inheritance). Airline-agnostic —
    /// whoever's allocation reaches the row edge inherits the bound. Null keeps the
    /// derived behavior, so existing walls are unaffected.
    /// </summary>
    public double? AllocationLeftPx { get; set; }

    /// <summary>
    /// Counterpart of AllocationLeftPx for the cluster containing this row's last
    /// counter. When it pins the wall's last cluster, the layer shell's
    /// extend-to-viewport-edge behavior is suppressed (IsRightmost = false).
    /// </summary>
    public double? AllocationRightPx { get; set; }

    /// <summary>
    /// Number of times this layer's image tiles horizontally across the canvas.
    /// Defaults to 1 (no tiling). When > 1, TblDisplay.DisplayWidth stores the per-tile
    /// image width (CanvasWidth / HorizontalImageLoop), and the admin UI renders that
    /// display N times side-by-side to fill the full canvas.
    /// </summary>
    public int HorizontalImageLoop { get; set; } = 1;

    /// <summary>
    /// Returns the gap (in layout pixels) that follows display at <paramref name="displayIndex"/>.
    /// Uses the cyclic <see cref="Gaps"/> pattern; falls back to <see cref="Spacing"/> when Gaps is empty.
    /// </summary>
    public double GetGapAfter(int displayIndex)
        => Gaps.Count > 0 ? Gaps[displayIndex % Gaps.Count] : Spacing;

    /// <summary>
    /// Returns the accumulated left pixel offset of the display at <paramref name="displayIndex"/>
    /// within the group, given an array of per-display widths (already scaled as needed).
    /// Accounts for the cyclic gap pattern.
    /// </summary>
    public double ComputeAccumulatedLeft(int displayIndex, IReadOnlyList<double> scaledWidths)
    {
        double left = PositionLeft;
        for (int i = 0; i < displayIndex; i++)
        {
            left += scaledWidths[i];
            left += GetGapAfter(i);
        }
        return left;
    }
}
