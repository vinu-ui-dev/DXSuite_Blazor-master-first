namespace DXSuite.Director.Models.BrightSign
{
    public class PlayerContentItem
    {
        public string? FileName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public int? Duration { get; set; } = 7;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public bool IsWeb { get; set; } = false;
        public int? PlayOrder { get; set; }
        public int? Weight { get; set; }
        public string? Days { get; set; }
        public string? WebRenderMode { get; set; }

        // Stream properties
        public bool IsStream { get; set; } = false;
        public string? StreamProtocol { get; set; }
        public bool StreamAutoReconnect { get; set; } = true;
        public int? StreamBufferMs { get; set; }

        // Ink-specific channel metadata. AssetName is the friendly channel label;
        // AssetInkPath carries the UDP multicast (or other Ink-readable) source URL
        // for TV-controller workflows where the visible asset is a channel logo image.
        public string? AssetName { get; set; }
        public string? AssetInkPath { get; set; }

        /// <summary>
        /// Set on a cached publication item whose URL must carry the requesting device's id, which
        /// is the one part of a display's content that cannot be shared between devices. Never
        /// serialized: it is a note to the delivery step, not a field the player has any use for.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool AppendDeviceId { get; set; }

        /// <summary>
        /// On a cached playlist publication, every tag the asset carries, or null for a file with no
        /// asset row. The publication holds the full set, including keys the location does not
        /// target, so a rule edit never invalidates it. Delivery targets on it per player and clears
        /// it, so it is never serialized.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyDictionary<string, IReadOnlySet<string>>? MetadataValues { get; set; }

        /// <summary>
        /// Delivery resolution mutates duration and clears the window fields, and the item it
        /// starts from belongs to a cached publication shared by every device at the location.
        /// </summary>
        public PlayerContentItem Copy() => (PlayerContentItem)MemberwiseClone();
    }
}
