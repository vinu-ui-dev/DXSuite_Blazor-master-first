namespace DXSuite.Sentinel.Services.Mqtt;

/// <summary>
/// Configuration for the Sentinel MQTT control plane, bound from the "Sentinel:Mqtt"
/// configuration section. Disabled by default — the broker is optional dev/runtime
/// infrastructure and the rest of Sentinel runs without it.
/// </summary>
public class SentinelMqttOptions
{
    public const string SectionName = "Sentinel:Mqtt";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public bool UseTls { get; set; }
    public string ClientId { get; set; } = "dxsuite-sentinel";
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// When true (and MQTT is enabled), the gateway transport uses MQTT instead of the
    /// legacy WebSocket acceptor — i.e. <see cref="Gateway.IGatewayConnectionManager"/>
    /// resolves to the MQTT-backed implementation.
    /// </summary>
    public bool GatewayOverMqtt { get; set; } = true;

    /// <summary>Seconds without a heartbeat before a gateway is considered offline.</summary>
    public int GatewayHeartbeatTimeoutSeconds { get; set; } = 90;

    /// <summary>Seconds to wait for a gateway command/poll response before timing out.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
