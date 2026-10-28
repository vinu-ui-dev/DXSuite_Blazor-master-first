using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Routing;

/// <summary>
/// Unified command routing layer. All device commands flow through this abstraction
/// regardless of how the device is reached (LAN direct, gateway, heartbeat piggyback, or cloud platform).
/// The Service Tech engine calls RouteCommandAsync without knowing or caring about the communication path.
/// </summary>
public interface ICommandRouter
{
    /// <summary>
    /// Route and execute a command to a device instance, automatically selecting the best communication path
    /// </summary>
    Task<CommandResult> RouteCommandAsync(int deviceInstanceId, string commandId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Route and execute a command with explicit route preference
    /// </summary>
    Task<CommandResult> RouteCommandAsync(int deviceInstanceId, string commandId, Dictionary<string, object>? parameters, RoutePreference preference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Preference for command routing path selection
/// </summary>
public enum RoutePreference
{
    /// <summary>Auto-select best available route (default)</summary>
    Auto,
    /// <summary>Prefer Site Gateway (LAN direct) - fastest, lowest latency</summary>
    PreferGateway,
    /// <summary>Prefer heartbeat piggyback - for heartbeat-based devices, queued for next poll</summary>
    PreferHeartbeat,
    /// <summary>Prefer cloud platform API - fallback, higher latency but works when gateway is offline</summary>
    PreferCloudPlatform
}
