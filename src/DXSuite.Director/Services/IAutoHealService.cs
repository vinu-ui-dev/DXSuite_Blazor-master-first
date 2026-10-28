namespace DXSuite.Director.Services;

/// <summary>
/// Service for managing automatic device healing (power cycling unresponsive displays)
/// </summary>
public interface IAutoHealService
{
    /// <summary>
    /// Checks all devices with AutoHealEnabled and attempts recovery for those offline
    /// </summary>
    Task CheckAndHealDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to heal a specific device by power cycling
    /// </summary>
    Task<AutoHealResult> AttemptHealAsync(string deviceId, string? triggeredBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last heal attempt time for a device (for cooldown checking)
    /// </summary>
    Task<DateTime?> GetLastHealAttemptAsync(string deviceId);

    /// <summary>
    /// Gets healing statistics for reporting
    /// </summary>
    Task<AutoHealStats> GetStatsAsync();
}

/// <summary>
/// Result of an auto-heal attempt
/// </summary>
public class AutoHealResult
{
    public bool Success { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public long ElapsedMs { get; set; }
    public bool WasInCooldown { get; set; }
    public bool DeviceNotFound { get; set; }
    public bool AutoHealDisabled { get; set; }
}

/// <summary>
/// Statistics for auto-heal operations
/// </summary>
public class AutoHealStats
{
    public int TotalAttempts { get; set; }
    public int SuccessfulAttempts { get; set; }
    public int FailedAttempts { get; set; }
    public int DevicesWithAutoHealEnabled { get; set; }
    public int DevicesCurrentlyOffline { get; set; }
    public DateTime? LastAttemptTime { get; set; }
}
