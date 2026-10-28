using System;
using System.Text.Json.Serialization;

namespace DXSuite.Director.Services;

/// <summary>
/// DTO for device ExtendedData JSON column.
/// Contains device-specific data that doesn't warrant dedicated columns.
/// </summary>
public class DeviceExtendedData
{
    [JsonPropertyName("contentHealth")]
    public ContentHealthData? ContentHealth { get; set; }

    [JsonPropertyName("connectedDisplays")]
    public ConnectedDisplaysData? ConnectedDisplays { get; set; }

    [JsonPropertyName("inputStatus")]
    public InputStatusData? InputStatus { get; set; }

    [JsonPropertyName("screenshot")]
    public ScreenshotData? Screenshot { get; set; }

    [JsonPropertyName("consoleLogging")]
    public ConsoleLoggingData? ConsoleLogging { get; set; }

    [JsonPropertyName("browser")]
    public BrowserData? Browser { get; set; }

    [JsonPropertyName("videoMode")]
    public VideoModeData? VideoMode { get; set; }

    [JsonPropertyName("windowMode")]
    public WindowModeData? WindowMode { get; set; }

    [JsonPropertyName("contentSync")]
    public ContentSyncData? ContentSync { get; set; }

    [JsonPropertyName("timeZoneFingerprint")]
    public TimeZoneFingerprintData? TimeZoneFingerprint { get; set; }

    [JsonPropertyName("updateProgress")]
    public UpdateProgressData? UpdateProgress { get; set; }

    [JsonPropertyName("declaredFlavour")]
    public DeclaredFlavourData? DeclaredFlavour { get; set; }
}

/// <summary>
/// The flavour a device last said it was running.
///
/// Recorded rather than acted on. Flavour is written to the device row from an approved pairing
/// request, so a heartbeat that contradicts it is a fault to show a human: the device is either
/// mis-provisioned or not ours. Storing the declaration is what lets the mismatch be derived on
/// read, so there is no flag to clear after a reflavour and no way for it to go stale.
/// </summary>
public class DeclaredFlavourData
{
    [JsonPropertyName("platformName")]
    public string? PlatformName { get; set; }

    [JsonPropertyName("reportedAt")]
    public DateTime? ReportedAt { get; set; }
}

/// <summary>
/// Which bundle a device's last update report came from.
///
/// One app update is reported by two different bundles: the running app owns UPDATE_STARTED and
/// DOWNLOAD_COMPLETE, and the swap takes under a second, so the bundle posting UPDATE_COMPLETE is
/// the new one with no memory of having started anything, or on a rollback the old one back again.
/// Without the stamp, an UPDATE_COMPLETE sitting beside a ClientAppVersion holding the old version
/// cannot be told apart from a rollback in flight.
/// </summary>
public class UpdateProgressData
{
    [JsonPropertyName("generation")]
    public int? Generation { get; set; }

    [JsonPropertyName("slot")]
    public string? Slot { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("artifactKind")]
    public string? ArtifactKind { get; set; }

    [JsonPropertyName("reportedAt")]
    public DateTime? ReportedAt { get; set; }
}

/// <summary>
/// What the player last reported about the time zone it is actually running: the UTC offsets in
/// force at two fixed instants, the year they were taken in, the POSIX literal in force, and the
/// literal the bootloader last applied.
///
/// An observation, not a verdict. Whether it agrees with the intended zone is derived on read by
/// TimeZoneVerification and never stored: Director has no device fault table, and a disagreement is
/// something to look at rather than an event to record.
/// </summary>
public class TimeZoneFingerprintData
{
    [JsonPropertyName("offsetJanuaryHours")]
    public decimal? OffsetJanuaryHours { get; set; }

    [JsonPropertyName("offsetJulyHours")]
    public decimal? OffsetJulyHours { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("posixString")]
    public string? PosixString { get; set; }

    /// <summary>From engage/tz_applied, which is what separates pending from mismatch.</summary>
    [JsonPropertyName("appliedLiteral")]
    public string? AppliedLiteral { get; set; }

    [JsonPropertyName("reportedAt")]
    public DateTime? ReportedAt { get; set; }
}

/// <summary>
/// Manifest version last echoed by the player, used by the UI to show whether a
/// device is playing current content (compare against the location's cached version).
/// </summary>
public class ContentSyncData
{
    [JsonPropertyName("manifestVersion")]
    public string? ManifestVersion { get; set; }

    [JsonPropertyName("reportedAt")]
    public DateTime? ReportedAt { get; set; }
}

/// <summary>
/// Content health monitoring data
/// </summary>
public class ContentHealthData
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("lastProof")]
    public DateTime? LastProof { get; set; }

    [JsonPropertyName("jsErrors")]
    public int? JsErrors { get; set; }
}

/// <summary>
/// Connected displays data (multi-output support)
/// </summary>
public class ConnectedDisplaysData
{
    /// <summary>
    /// Raw JSON array of connected display info
    /// Example: [{"output":"HDMI-1","connected":true,"resolution":"1920x1080x60p","manufacturer":"LG","model":"55UH5F"}]
    /// </summary>
    [JsonPropertyName("displays")]
    public string? Displays { get; set; }

    [JsonPropertyName("lastChanged")]
    public DateTime? LastChanged { get; set; }
}

/// <summary>
/// Input status tracking (for detecting wrong input source)
/// </summary>
public class InputStatusData
{
    /// <summary>
    /// 'foreground' or 'background'
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("changedAt")]
    public DateTime? ChangedAt { get; set; }
}

/// <summary>
/// Screenshot data for on-demand device screen capture
/// </summary>
public class ScreenshotData
{
    /// <summary>
    /// Relative path to the screenshot file (e.g., "/DeviceScreenshots/{deviceId}/screenshot.jpg")
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// When the screenshot was captured
    /// </summary>
    [JsonPropertyName("capturedAt")]
    public DateTime? CapturedAt { get; set; }

    /// <summary>
    /// Size of the screenshot file in bytes
    /// </summary>
    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }
}

/// <summary>
/// Console logging settings and recent log entries for remote debugging
/// </summary>
public class ConsoleLoggingData
{
    /// <summary>
    /// Whether console log collection is enabled for this device
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// When logging was last enabled/disabled
    /// </summary>
    [JsonPropertyName("toggledAt")]
    public DateTime? ToggledAt { get; set; }

    /// <summary>
    /// Recent console log entries (kept pruned to last N entries)
    /// </summary>
    [JsonPropertyName("entries")]
    public List<ConsoleLogEntry>? Entries { get; set; }
}

/// <summary>
/// A single console log entry from a device
/// </summary>
public class ConsoleLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "log";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Per-device video playback mode override
/// </summary>
public class VideoModeData
{
    /// <summary>
    /// When true, the player uses HTML5 video instead of the native SOS video API.
    /// Use to work around platform-specific video loop bugs on WebOS/Tizen.
    /// </summary>
    [JsonPropertyName("forceHtml5")]
    public bool ForceHtml5 { get; set; }
}

/// <summary>
/// Window display mode for Avalonia thick-client players.
/// Ignored by SignageOS/browser-based players.
/// </summary>
public class WindowModeData
{
    /// <summary>Display mode: "fullscreen", "windowed", or "borderless"</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "fullscreen";

    /// <summary>Top-left X coordinate in pixels (windowed/borderless only)</summary>
    [JsonPropertyName("x")]
    public int? X { get; set; }

    /// <summary>Top-left Y coordinate in pixels (windowed/borderless only)</summary>
    [JsonPropertyName("y")]
    public int? Y { get; set; }

    /// <summary>Width in pixels (windowed/borderless only)</summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    /// <summary>Height in pixels (windowed/borderless only)</summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }
}

/// <summary>
/// Browser information reported by SignageOS/WebOS clients via navigator.userAgent
/// </summary>
public class BrowserData
{
    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
