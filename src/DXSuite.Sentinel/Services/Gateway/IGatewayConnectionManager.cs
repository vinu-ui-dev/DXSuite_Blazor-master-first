using System.Net.WebSockets;
using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Gateway;

/// <summary>
/// Manages WebSocket connections to Site Gateways deployed at customer locations.
/// Handles command routing through gateways, gateway health monitoring, and event reception.
/// </summary>
public interface IGatewayConnectionManager
{
    /// <summary>
    /// Check if a gateway is currently connected and healthy
    /// </summary>
    bool IsGatewayOnline(int gatewayId);

    /// <summary>
    /// Send a command to a device through its site gateway
    /// </summary>
    Task<CommandResult> SendCommandViaGatewayAsync(int gatewayId, string deviceIp, int? devicePort, string commandId, Dictionary<string, object>? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request a device state poll through the gateway
    /// </summary>
    Task<DeviceState?> RequestDevicePollAsync(int gatewayId, string deviceIp, int? devicePort, string descriptorJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run a Task script on the gateway/agent host itself and return the exit code + captured output.
    /// </summary>
    Task<CommandResult> SendScriptViaGatewayAsync(int gatewayId, string scriptType, string scriptContent, string scriptHash, int? timeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the list of currently connected gateways
    /// </summary>
    IReadOnlyList<GatewayConnectionInfo> GetConnectedGateways();

    /// <summary>
    /// Register a gateway WebSocket connection. Blocks until the connection closes.
    /// </summary>
    Task RegisterGatewayAsync(int gatewayId, WebSocket webSocket, GatewayConnectionInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a disconnected gateway
    /// </summary>
    Task UnregisterGatewayAsync(int gatewayId);
}

public class GatewayConnectionInfo
{
    public int GatewayId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public DateTime ConnectedSince { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public string? GatewayVersion { get; set; }
    public string? RemoteIpAddress { get; set; }
}
