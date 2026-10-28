namespace DXSuite.Director.Models.ZoneContent
{
    /// <summary>
    /// Content for a single zone (display) in a multi-zone layout
    /// </summary>
    public class ZoneDisplayContent
    {
        /// <summary>
        /// The display ID for this zone
        /// </summary>
        public int DisplayId { get; set; }

        /// <summary>
        /// Zone number (1-8 for hotel-test layout)
        /// </summary>
        public int ZoneNumber { get; set; }

        /// <summary>
        /// Human-readable zone name (e.g., "Branding", "Weather", "Video")
        /// </summary>
        public string ZoneName { get; set; } = string.Empty;

        /// <summary>
        /// Zone type: "playlist" for media content, "weather" for weather widget
        /// </summary>
        public string ZoneType { get; set; } = "playlist";

        /// <summary>
        /// Layout positioning for this zone (percentage-based)
        /// </summary>
        public ZoneLayoutPosition? Layout { get; set; }

        /// <summary>
        /// Content items for playlist zones
        /// </summary>
        public List<ZoneContentItem> Content { get; set; } = new();

        /// <summary>
        /// The Playlist location whose metadata rules target this zone's content. Held only in the
        /// cached publication so delivery can target per device; always null on the wire. Null for
        /// card zones and for any source that is not a Playlist location, neither of which is targeted.
        /// </summary>
        public int? TargetingLocationId { get; set; }
    }

    /// <summary>
    /// Zone layout positioning (percentage-based for resolution independence)
    /// </summary>
    public class ZoneLayoutPosition
    {
        /// <summary>
        /// Left position as percentage (0-100)
        /// </summary>
        public decimal X { get; set; }

        /// <summary>
        /// Top position as percentage (0-100)
        /// </summary>
        public decimal Y { get; set; }

        /// <summary>
        /// Width as percentage (0-100)
        /// </summary>
        public decimal Width { get; set; }

        /// <summary>
        /// Height as percentage (0-100)
        /// </summary>
        public decimal Height { get; set; }
    }

    /// <summary>
    /// A single content item within a zone
    /// </summary>
    public class ZoneContentItem
    {
        /// <summary>
        /// URL to the content (relative or absolute)
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Content type: "image" or "video"
        /// </summary>
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// Display duration in seconds (for images)
        /// </summary>
        public int Duration { get; set; } = 7;

        /// <summary>
        /// Asset ID reference (optional, for tracking)
        /// </summary>
        public int? AssetId { get; set; }

        /// <summary>
        /// Every tag the asset carries, or null for a file with no asset row. Held only in the cached
        /// publication so delivery can target per device; always null on the wire.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlySet<string>>? MetadataValues { get; set; }
    }
}
