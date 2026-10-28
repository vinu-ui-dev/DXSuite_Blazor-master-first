using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Contracts;

namespace DXSuite.Sentinel.Services.Mqtt;

/// <summary>
/// Subscribes to inbound device and gateway telemetry on the MQTT control plane.
/// For now it logs what arrives, proving the subscribe/route path end-to-end; Phase 5
/// replaces these handlers with real ingestion (update DeviceInstance/SiteGateway state,
/// LWT-driven offline marking, and an evaluation trigger).
/// </summary>
public class SentinelIngestionService : IHostedService
{
    private readonly ISentinelMessageBus _bus;
    private readonly SentinelMqttOptions _options;
    private readonly AgentHostRegistry _agentHosts;
    private readonly ILogger<SentinelIngestionService> _logger;

    public SentinelIngestionService(
        ISentinelMessageBus bus,
        SentinelMqttOptions options,
        AgentHostRegistry agentHosts,
        ILogger<SentinelIngestionService> logger)
    {
        _bus = bus;
        _options = options;
        _agentHosts = agentHosts;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        // Gateway state/lwt are owned by MqttGatewayConnectionManager (it tracks liveness and
        // persists SiteGateway status). Ingestion handles device telemetry.
        await _bus.SubscribeAsync(SentinelTopics.AllDeviceState, OnDeviceState, cancellationToken);
        await _bus.SubscribeAsync(SentinelTopics.AllDeviceHealth, OnDeviceHealth, cancellationToken);
        await _bus.SubscribeAsync(SentinelTopics.AllDeviceLwt, OnLastWill, cancellationToken);

        _logger.LogInformation("Sentinel ingestion subscribed to device telemetry topics");
    }

    private async Task OnDeviceState(SentinelMessage m)
    {
        var msg = SafeDeserialize<DeviceStateMessage>(m.Payload);
        if (msg is null) return;

        // Self-monitoring agents carry their owning GatewayId — represent the host as a DeviceInstance.
        if (msg.GatewayId.HasValue)
            await _agentHosts.UpsertFromStateAsync(msg);
        else
            _logger.LogDebug("Ingest device state [{Topic}]: {Payload}", m.Topic, Truncate(m.Payload));
    }

    private Task OnDeviceHealth(SentinelMessage m)
    {
        // Health is the time-series beat; state already carries the latest snapshot used for the
        // DeviceInstance. Trend storage (ProbeHistory-style) is a later refinement.
        _logger.LogDebug("Ingest device health [{Topic}]", m.Topic);
        return Task.CompletedTask;
    }

    private static T? SafeDeserialize<T>(string json)
    {
        try { return SentinelJson.Deserialize<T>(json); }
        catch { return default; }
    }

    private Task OnLastWill(SentinelMessage m)
    {
        // LWT fires when a device/gateway drops ungracefully — the fast offline signal.
        _logger.LogWarning("Ingest LWT (offline) [{Topic}]: {Payload}", m.Topic, Truncate(m.Payload));
        return Task.CompletedTask;
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
