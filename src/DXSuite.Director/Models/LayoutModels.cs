namespace DXSuite.Director.Models;

/// <summary>
/// Configuration for a Layout location, stored in TblLocation.ExtendedData as JSON.
/// Layout locations compose multiple Playlist/Card locations into positioned zones.
/// </summary>
public class LayoutConfiguration
{
    /// <summary>
    /// Overall layout aspect ratio width (e.g., 16 for 16:9)
    /// </summary>
    public decimal AspectRatioWidth { get; set; } = 16;

    /// <summary>
    /// Overall layout aspect ratio height (e.g., 9 for 16:9)
    /// </summary>
    public decimal AspectRatioHeight { get; set; } = 9;

    /// <summary>
    /// List of zones in this layout
    /// </summary>
    public List<LayoutZone> Zones { get; set; } = new();
}

/// <summary>
/// A zone within a layout, linking to an existing Playlist or Card location
/// </summary>
public class LayoutZone
{
    /// <summary>
    /// Zone number for ordering and identification
    /// </summary>
    public int ZoneNumber { get; set; }

    /// <summary>
    /// The source location ID (Playlist or Card location)
    /// </summary>
    public int SourceLocationId { get; set; }

    /// <summary>
    /// Display name of the source location
    /// </summary>
    public string SourceLocationName { get; set; } = "";

    /// <summary>
    /// For Card locations only - specifies which display within the location.
    /// Null for Playlist locations.
    /// </summary>
    public int? SourceDisplayId { get; set; }

    /// <summary>
    /// For Card locations - the display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Source width from TblLocation.BackgroundImageWidth (Playlist) or TblDisplay.DisplayWidth (Card)
    /// </summary>
    public decimal SourceWidth { get; set; }

    /// <summary>
    /// Source height from TblLocation.BackgroundImageHeight (Playlist) or TblDisplay.DisplayHeight (Card)
    /// </summary>
    public decimal SourceHeight { get; set; }

    /// <summary>
    /// Top position in pixels from the layout origin
    /// </summary>
    public decimal Top { get; set; }

    /// <summary>
    /// Left position in pixels from the layout origin
    /// </summary>
    public decimal Left { get; set; }

    /// <summary>
    /// Width in pixels
    /// </summary>
    public decimal Width { get; set; }

    /// <summary>
    /// Height in pixels
    /// </summary>
    public decimal Height { get; set; }
}
