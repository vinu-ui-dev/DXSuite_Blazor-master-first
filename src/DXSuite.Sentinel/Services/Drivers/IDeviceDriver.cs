using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Drivers;

/// <summary>
/// Interface for device communication drivers. Each protocol (TCP RS-232, QRC, HTTP, SNMP, ICMP)
/// has a driver implementation that knows how to poll device state and execute commands.
/// </summary>
public interface IDeviceDriver
{
    /// <summary>
    /// Protocol identifier this driver handles (e.g., "tcp-rs232", "qrc", "http-heartbeat", "icmp")
    /// </summary>
    string ProtocolId { get; }

    /// <summary>
    /// Poll the device for its current state based on the descriptor's monitor points
    /// </summary>
    Task<DeviceState> PollAsync(DeviceInstanceInfo device, DeviceDescriptorSchema descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a command on the device
    /// </summary>
    Task<CommandResult> ExecuteCommandAsync(DeviceInstanceInfo device, string commandId, Dictionary<string, object>? parameters, DeviceDescriptorSchema descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connectivity to the device
    /// </summary>
    Task<bool> TestConnectionAsync(DeviceInstanceInfo device, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight device instance info passed to drivers (avoids passing full EF entities)
/// </summary>
public class DeviceInstanceInfo
{
    public int DeviceInstanceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public string? MacAddress { get; set; }
    public Dictionary<string, string>? Credentials { get; set; }
    public Dictionary<string, object>? Config { get; set; }
}
