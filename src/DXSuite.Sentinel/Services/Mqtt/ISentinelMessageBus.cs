namespace DXSuite.Sentinel.Services.Mqtt;

/// <summary>A message received from the broker: its topic and string payload.</summary>
public record SentinelMessage(string Topic, string Payload);

/// <summary>
/// Transport seam over the MQTT broker. Higher layers (gateway transport, device
/// telemetry ingestion, command routing) publish and subscribe through this so they
/// never depend on MQTTnet types directly — and so the transport can be swapped or
/// faked in tests. Subscriptions survive reconnects: registered filters are
/// re-subscribed automatically whenever the broker session is re-established.
/// </summary>
public interface ISentinelMessageBus
{
    bool IsConnected { get; }

    /// <summary>Publish a payload to a topic (QoS 1). Best-effort: no-ops with a warning if not connected.</summary>
    Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default);

    /// <summary>Register a handler for a topic filter (MQTT wildcards allowed). Re-applied on every reconnect.</summary>
    Task SubscribeAsync(string topicFilter, Func<SentinelMessage, Task> handler, CancellationToken cancellationToken = default);
}
