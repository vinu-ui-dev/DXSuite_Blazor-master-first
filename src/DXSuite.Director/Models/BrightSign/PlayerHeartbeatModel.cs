using DXSuite.Director.Services;

namespace DXSuite.Director.Models.BrightSign
{
    public class PlayerHeartbeatModel
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Model {  get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        //public int? LocationId { get; set; }
        public string? StorageType { get; set; } = string.Empty;
        public long? StorageSize { get; set; }
        public int? StorageHealth { get; set; }
        public string? ClientAppVersion { get; set; }

        public string? ClientBootloaderVersion { get; set; }

        // Input Status Tracking (for detecting wrong input source on WebOS)
        public string? InputStatus { get; set; } // 'foreground' or 'background'
        public long? InputStatusChangedAt { get; set; } // Unix timestamp (ms)

        // HDMI Status Monitoring (BrightSign)
        public bool? HdmiConnected { get; set; }
        public bool? HdmiPowered { get; set; } // Display power state (if detectable)
        public string? HdmiResolution { get; set; }
        public HdmiEdidInfo? HdmiEdid { get; set; }
        public long? HdmiLastChanged { get; set; } // Unix timestamp (ms)

        // Extended device properties (JSON blob from SignageOS/WebOS clients)
        public string? PropertiesJson { get; set; }

        // Content Health Monitoring
        public ContentHealthStatus? ContentHealth { get; set; }
        public ContentHealthEvent? ContentHealthEvent { get; set; }

        // Console Logging (for remote debugging)
        public List<ConsoleLogEntry>? ConsoleLogs { get; set; }

        /// <summary>
        /// Player platform identifier sent by the client application.
        /// Avalonia players send "avalonia"; SignageOS players send "signageos" or omit this field.
        /// Used to auto-create TblAppPlatform entries and enable platform-specific UI controls.
        /// </summary>
        public string? ClientPlatform { get; set; }

        /// <summary>
        /// Alias for <see cref="ClientPlatform"/>. Engage for BrightSign declares its flavour as
        /// "platform"; binding it here rather than adding a second identity field means the
        /// resolver, the mismatch check and the applet all read one value.
        /// </summary>
        public string? Platform
        {
            get => ClientPlatform;
            set => ClientPlatform = value;
        }

        /// <summary>
        /// Manifest version hash from the last full content response this player applied.
        /// When it matches current content the server answers NotModified. Absent from
        /// pre-1.1.0 players, which always receive the full manifest.
        /// </summary>
        public string? ManifestVersion { get; set; }

        /// <summary>
        /// The asset the player reports as currently on screen. Heartbeat fallback for the
        /// real-time wake-channel report; absent from pre-1.2.0 players.
        /// </summary>
        public CurrentAssetInfo? CurrentAsset { get; set; }
    }

    /// <summary>
    /// Now-playing report shared by the wake-channel hub method and the heartbeat field.
    /// </summary>
    public class CurrentAssetInfo
    {
        public string? Url { get; set; }
        public string? Name { get; set; }
        public int Index { get; set; }
        public int Total { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public double? DurationSeconds { get; set; }
    }

    /// <summary>
    /// Current health status of web content displayed in iframe
    /// </summary>
    public class ContentHealthStatus
    {
        public string? Url { get; set; }
        public string? Status { get; set; } // loading | healthy | unresponsive | failed
        public long? LastProofOfLife { get; set; }
        public int JsErrors { get; set; }
    }

    /// <summary>
    /// State change event for content health (only sent when status transitions)
    /// </summary>
    public class ContentHealthEvent
    {
        public string? DeviceId { get; set; }
        public string? ContentUrl { get; set; }
        public string? PreviousStatus { get; set; }
        public string? Status { get; set; }
        public string? Reason { get; set; }
        public long? Timestamp { get; set; }
    }

    /// <summary>
    /// HDMI EDID information from connected display
    /// </summary>
    public class HdmiEdidInfo
    {
        public string? Manufacturer { get; set; }
        public string? ProductName { get; set; }
        public string? SerialNumber { get; set; }
    }
}
