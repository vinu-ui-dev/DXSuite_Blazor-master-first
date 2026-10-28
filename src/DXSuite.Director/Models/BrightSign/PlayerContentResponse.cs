using DXSuite.Director.Entities;
using DXSuite.Director.Models.ZoneContent;

namespace DXSuite.Director.Models.BrightSign
{
    public class PlayerContentResponse
    {
        public int? LocationId { get; set; }
        public int? DisplayId { get; set; }
        public string? Status { get; set; }
        public bool Shuffle { get; set; } = false;
        public bool EnableFade { get; set; } = true;
        public List<PlayerContentItem> Assets { get; set; } = new();

        /// <summary>
        /// Content hash of the manifest portion of this response. Players persist and echo it
        /// in the next heartbeat's manifestVersion. Null when an override is active or the
        /// response is not a cacheable manifest, which makes the player echo nothing and
        /// receive a full manifest on its next poll.
        /// </summary>
        public string? ManifestVersion { get; set; }

        /// <summary>
        /// The complete SetTimeZone argument for this device's operator-confirmed zone, or null
        /// when no operator has confirmed one, which the player reads as nothing having told it yet.
        ///
        /// It is state rather than a command: re-asserted on every poll, including the NotModified
        /// path, so a player that was offline or re-imaged is corrected by the next poll instead of
        /// missing a delivery it can never get back. Outside the manifest hash, like every other
        /// per-device field on this response.
        /// </summary>
        public string? OsTimeZone { get; set; }

        /// <summary>
        /// The roHtmlWidget transform for this device's Orientation (identity, rot90, rot270 or
        /// rot180). Null only when the device is not registered. State like OsTimeZone: re-asserted
        /// on every poll and outside the manifest hash.
        /// </summary>
        public string? Orientation { get; set; }

        /// <summary>
        /// How this device draws video: "hardware" or "page". Null only when the device is not
        /// registered. State like Orientation: re-asserted on every poll and outside the manifest hash.
        /// </summary>
        public string? VideoPath { get; set; }

        /// <summary>
        /// True when the player's echoed manifestVersion matches current content. Assets and
        /// Zones are omitted; the player keeps its current playlist. Command fields
        /// (screenshot, CEC, flush, logging, update) are still populated and must be processed.
        /// </summary>
        public bool NotModified { get; set; } = false;

        // Override-related properties
        public bool IsOverride { get; set; } = false;
        public int? OverrideId { get; set; }
        public string? OverrideName { get; set; }
        public UpdateInfo? Update { get; set; }

        // Beside Update rather than inside an array. The player branches on the two kinds
        // regardless, and the server never populates both in one response.
        public UpdateInfo? BootloaderUpdate { get; set; }
        public CecCommandInfo? CecCommand { get; set; }
        public ScreenshotCommandInfo? ScreenshotCommand { get; set; }

        /// <summary>
        /// Whether console log collection is enabled for this device.
        /// When true, the client should capture console logs and send them with heartbeats.
        /// </summary>
        public bool ConsoleLoggingEnabled { get; set; } = false;

        /// <summary>
        /// Forces HTML5 video playback instead of the native SOS video API.
        /// Set per-device to work around platform-specific video loop bugs on WebOS/Tizen.
        /// </summary>
        public bool ForceHtml5Video { get; set; } = false;

        /// <summary>
        /// Instructs the client to clear all locally cached media assets and re-download
        /// the full manifest on the next sync. One-shot: queued from the admin UI and
        /// delivered once on the device's next heartbeat.
        /// </summary>
        public bool FlushCache { get; set; } = false;

        /// <summary>
        /// Whether the client may start displaying assets using their original HTTP URLs
        /// while local file downloads are still in progress. When false, the client waits
        /// for all assets to be cached before starting playback. Disable for cloud-hosted
        /// deployments where serving the same content twice generates billable egress.
        /// </summary>
        public bool AllowHttpFallback { get; set; } = true;

        /// <summary>
        /// Server-driven poll cadence in seconds. 1.1.0+ players re-pace their sync loop to
        /// this value (clamped client-side); older players ignore it and stay at 30s.
        /// </summary>
        public int? NextPollSeconds { get; set; }

        /// <summary>
        /// Server-driven minutes between full device-inventory heartbeats; 0 requests the
        /// full payload on every beat. Older players always send full and ignore this.
        /// </summary>
        public int? FullInventoryMinutes { get; set; }

        /// <summary>
        /// Set when this device is unknown to the DB so a light-beating player sends its
        /// full inventory on the next poll and auto-registration can complete.
        /// </summary>
        public bool RequestFullInventory { get; set; } = false;

        // Zone-based content for multi-zone layouts (hotel-test, etc.)
        /// <summary>
        /// Device metadata as URL parameters (e.g., zipCode, propertyName)
        /// These are injected into web content URLs by the client
        /// </summary>
        public Dictionary<string, string>? DeviceMetadata { get; set; }

        /// <summary>
        /// Content for each zone at this location (for multi-zone layouts)
        /// </summary>
        public List<ZoneDisplayContent>? Zones { get; set; }

        /// <summary>
        /// Layout design width in pixels (for scaling zones to actual screen)
        /// </summary>
        public int? LayoutWidth { get; set; }

        /// <summary>
        /// Layout design height in pixels (for scaling zones to actual screen)
        /// </summary>
        public int? LayoutHeight { get; set; }

        /// <summary>
        /// Window display mode for Avalonia thick-client players (fullscreen, windowed, borderless).
        /// Null for non-Avalonia players; those clients can safely ignore this field.
        /// </summary>
        public WindowModeResponseData? WindowMode { get; set; }
    }

    /// <summary>
    /// Window display mode settings delivered to Avalonia players via heartbeat response.
    /// </summary>
    public class WindowModeResponseData
    {
        /// <summary>"fullscreen", "windowed", or "borderless"</summary>
        public string Mode { get; set; } = "fullscreen";

        /// <summary>Top-left X coordinate in pixels (windowed/borderless only)</summary>
        public int? X { get; set; }

        /// <summary>Top-left Y coordinate in pixels (windowed/borderless only)</summary>
        public int? Y { get; set; }

        /// <summary>Width in pixels (windowed/borderless only)</summary>
        public int? Width { get; set; }

        /// <summary>Height in pixels (windowed/borderless only)</summary>
        public int? Height { get; set; }
    }

    /// <summary>
    /// CEC command to be executed by the device
    /// </summary>
    public class CecCommandInfo
    {
        /// <summary>
        /// The action to perform: "power_on" or "power_off"
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Target output (e.g., "HDMI-1")
        /// </summary>
        public string? Output { get; set; }
    }

    public class UpdateInfo
    {
        public bool UpdateAvailable { get; set; }
        public string? TargetVersion { get; set; }
        public int? AppVersionId { get; set; }
        public string? DownloadUrl { get; set; }
        public long? FileSize { get; set; }
        public string? FileHash { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool RequiresReboot { get; set; } = true;
        public bool TriggerUpdateNow { get; set; } = false;
        public string ArtifactKind { get; set; } = ArtifactKinds.App;

        /// <summary>
        /// The bootloader this bundle needs, carried so the device can refuse as a last line.
        /// App bundles only; null on a bootloader offer and on a bundle that names no floor.
        ///
        /// The server is the decision point and does not offer a bundle whose floor the device
        /// fails. This exists because a card can be moved between players and arrive under an
        /// older bootloader than the one it left, which the offer that was resolved for the
        /// previous player cannot know about.
        /// </summary>
        public string? MinBootloaderVersion { get; set; }
    }

    /// <summary>
    /// Screenshot capture command for on-demand screen capture
    /// </summary>
    public class ScreenshotCommandInfo
    {
        /// <summary>
        /// Unique identifier for this screenshot request
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Unix timestamp (milliseconds) when the request was made
        /// </summary>
        public long RequestedAt { get; set; }
    }
}
