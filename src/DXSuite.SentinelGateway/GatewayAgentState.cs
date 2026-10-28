namespace DXSuite.SentinelGateway;

/// <summary>
/// Live runtime state of the gateway agent, shared between the MQTT worker (writer)
/// and the local status/config web UI (reader). Singleton.
/// </summary>
public class GatewayAgentState
{
    // Static identity (from config)
    public string Broker { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public int GatewayId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Roles { get; set; } = string.Empty;

    // Live connection state
    public bool Connected { get; set; }
    public DateTime? ConnectedSinceUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }

    // Activity
    public long CommandsHandled { get; set; }
    public string? LastCommand { get; set; }
    public DateTime? LastCommandUtc { get; set; }

    // Self-monitor telemetry (selfmonitor role)
    public DateTime? LastTelemetryUtc { get; set; }
    public double? LastCpuPercent { get; set; }
    public double? LastMemoryUsedPercent { get; set; }
}
