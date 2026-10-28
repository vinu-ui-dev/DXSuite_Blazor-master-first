using System.Collections.Generic;

namespace DXSuite.Director.Models.Panels;

/// <summary>
/// One hotspot in a panel bundle bound to one display. Persisted as JSON on
/// <see cref="Entities.TblPanel.HotspotMapJson"/>.
/// </summary>
public class PanelHotspotMapping
{
    /// <summary>Stable key declared by the bundle manifest, e.g. "screen-1".</summary>
    public string HotspotKey { get; set; } = string.Empty;

    public int DisplayId { get; set; }
}

/// <summary>
/// The document published to the "public.panel" storage key and read by Moxie through
/// /api/player/bundle-state. The host does not interpret it, so its shape is owned by this
/// class and can change without touching the read endpoint.
/// </summary>
public class PanelPublishedDocument
{
    public int Schema { get; set; } = 1;
    public long Revision { get; set; }
    public string UpdatedUtc { get; set; } = string.Empty;
    public string PanelKey { get; set; } = string.Empty;
    public string PanelName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public List<PanelPublishedScreen> Screens { get; set; } = new();
}

/// <summary>
/// One screen's current assignment. Every mapped screen is always emitted, in hotspot order —
/// a cleared screen carries kind "none" rather than dropping out, so a consumer can tell
/// "deliberately blank" from "no longer mapped".
/// </summary>
public class PanelPublishedScreen
{
    public int Index { get; set; }
    public string HotspotKey { get; set; } = string.Empty;
    public int DisplayId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = AssetContentKind.None;
    public string Url { get; set; } = string.Empty;
    public int AssetId { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public int Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Boot payload handed to the panel bundle. Deliberately minimal: the panel is an anonymous
/// surface, so this carries nothing about users, tenants, or other locations.
/// </summary>
public class PanelContext
{
    public string PanelKey { get; set; } = string.Empty;
    public string PanelName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public List<PanelHotspotMapping> Hotspots { get; set; } = new();
    public int PollSeconds { get; set; } = 10;
}

/// <summary>One selectable item in the panel's shared Asset Library.</summary>
public class PanelAsset
{
    public int AssetId { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public string Kind { get; set; } = AssetContentKind.Image;
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Still image safe to render in a grid. Never a video URL: a panel runs in a player
    /// webzone where every video element is a software decode, so a grid of live video
    /// previews would flatten the zone.
    /// </summary>
    public string ThumbnailUrl { get; set; } = string.Empty;

    public int Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// The asset's content metadata, in the same shape the Location Content API returns it.
    ///
    /// Present so one bundle can drive both surfaces: a library grouped into folders by a metadata
    /// field has to group the same way on a panel, and without this a panel would quietly fall back
    /// to one flat list. Scoped to the panel's own library by the query that fills it.
    /// </summary>
    public List<PanelAssetMetadata> Metadata { get; set; } = new();
}

public class PanelAssetMetadata
{
    public string MetadataKey { get; set; } = string.Empty;
    public string MetadataValue { get; set; } = string.Empty;
}

/// <summary>Current state of one mapped screen, as the panel UI needs it.</summary>
public class PanelDisplayState
{
    public string HotspotKey { get; set; } = string.Empty;
    public int DisplayId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int AssetId { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public string Kind { get; set; } = AssetContentKind.None;
    public string ThumbnailUrl { get; set; } = string.Empty;
}

/// <summary>Write payload. A null or absent AssetId clears the screen.</summary>
public class PanelSetContentRequest
{
    public int? AssetId { get; set; }
}
