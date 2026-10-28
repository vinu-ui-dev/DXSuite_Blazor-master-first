using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Contracts;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Mqtt;

namespace DXSuite.Sentinel.Services.Gateway;

/// <summary>
/// MQTT-backed implementation of <see cref="IGatewayConnectionManager"/>. Gateways connect
/// outbound to the broker (not to this host), so "online" is tracked from their retained
/// heartbeat on .../gw/{id}/state and their Last Will on .../gw/{id}/lwt. Commands are
/// published to .../gw/{id}/cmd and replies are correlated by an in-payload RequestId
/// arriving on .../gw/{id}/resp.
///
/// Selected over the WebSocket manager when Sentinel:Mqtt:Enabled and GatewayOverMqtt are set.
/// The WebSocket lifecycle methods are intentionally inert in this mode.
/// </summary>
public class MqttGatewayConnectionManager : IGatewayConnectionManager, IHostedService
{
    private readonly ISentinelMessageBus _bus;
    private readonly SentinelMqttOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentHostRegistry _agentHosts;
    private readonly ILogger<MqttGatewayConnectionManager> _logger;

    private readonly ConcurrentDictionary<int, GatewayRuntime> _gateways = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GatewayResponseMessage>> _pending = new();

    public MqttGatewayConnectionManager(
        ISentinelMessageBus bus,
        SentinelMqttOptions options,
        IServiceScopeFactory scopeFactory,
        AgentHostRegistry agentHosts,
        ILogger<MqttGatewayConnectionManager> logger)
    {
        _bus = bus;
        _options = options;
        _scopeFactory = scopeFactory;
        _agentHosts = agentHosts;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _bus.SubscribeAsync(SentinelTopics.AllGatewayState, OnHeartbeatAsync, cancellationToken);
        await _bus.SubscribeAsync(SentinelTopics.AllGatewayLwt, OnLastWillAsync, cancellationToken);
        await _bus.SubscribeAsync(SentinelTopics.AllGatewayResponse, OnResponseAsync, cancellationToken);
        _logger.LogInformation("MQTT gateway transport active (gateway state/lwt/resp subscriptions registered)");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── Inbound from gateways ──

    private async Task OnHeartbeatAsync(SentinelMessage m)
    {
        if (!SentinelTopics.TryParse(m.Topic, out var tenantId, out var type, out var idStr, out _)
            || type != "gw" || !int.TryParse(idStr, out var gatewayId))
            return;

        var hb = SafeDeserialize<GatewayHeartbeatMessage>(m.Payload);
        var rt = _gateways.GetOrAdd(gatewayId, id => new GatewayRuntime { GatewayId = id });
        var cameOnline = rt.Offline || rt.LastHeartbeat == default;
        rt.TenantId = tenantId;
        rt.Version = hb?.Version;
        rt.LastHeartbeat = DateTime.UtcNow;
        rt.Offline = false;

        if (cameOnline)
            _logger.LogInformation("Gateway {GatewayId} online (version {Version})", gatewayId, hb?.Version ?? "?");

        await PersistGatewayStatusAsync(gatewayId, online: true, version: hb?.Version);
    }

    private async Task OnLastWillAsync(SentinelMessage m)
    {
        if (!SentinelTopics.TryParse(m.Topic, out _, out var type, out var idStr, out _)
            || type != "gw" || !int.TryParse(idStr, out var gatewayId))
            return;

        if (_gateways.TryGetValue(gatewayId, out var rt))
            rt.Offline = true;
        _logger.LogWarning("Gateway {GatewayId} Last Will received — marked offline", gatewayId);
        await PersistGatewayStatusAsync(gatewayId, online: false, version: null);
        // One MQTT connection carries one Will (the gateway LWT), so a self-monitoring agent's host
        // device has no LWT of its own — propagate the gateway drop to its host device here.
        await _agentHosts.MarkOfflineByGatewayAsync(gatewayId);
    }

    /// <summary>
    /// Mirrors live connection state onto the SiteGateway row so the Gateways tab shows
    /// accurate status/version/last-heartbeat. No-ops if the gateway hasn't been provisioned
    /// in this tenant yet (an agent announcing an unknown id is simply ignored here).
    /// </summary>
    private async Task PersistGatewayStatusAsync(int gatewayId, bool online, string? version)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            var gw = await db.SiteGateways.FirstOrDefaultAsync(g => g.GatewayId == gatewayId);
            if (gw is null)
                return;

            gw.Status = online ? "connected" : "disconnected";
            if (online)
            {
                gw.LastHeartbeat = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(version))
                    gw.Version = version;
            }
            gw.LastEditDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Persisting gateway {GatewayId} status failed: {Message}", gatewayId, ex.Message);
        }
    }

    private Task OnResponseAsync(SentinelMessage m)
    {
        var resp = SafeDeserialize<GatewayResponseMessage>(m.Payload);
        if (resp is not null && !string.IsNullOrEmpty(resp.RequestId)
            && _pending.TryRemove(resp.RequestId, out var tcs))
            tcs.TrySetResult(resp);
        return Task.CompletedTask;
    }

    // ── IGatewayConnectionManager ──

    public bool IsGatewayOnline(int gatewayId)
    {
        if (!_gateways.TryGetValue(gatewayId, out var rt) || rt.Offline)
            return false;
        return (DateTime.UtcNow - rt.LastHeartbeat).TotalSeconds < _options.GatewayHeartbeatTimeoutSeconds;
    }

    public IReadOnlyList<GatewayConnectionInfo> GetConnectedGateways() =>
        _gateways.Values
            .Where(rt => IsGatewayOnline(rt.GatewayId))
            .Select(rt => new GatewayConnectionInfo
            {
                GatewayId = rt.GatewayId,
                CustomerId = rt.TenantId,
                ConnectedSince = rt.LastHeartbeat,
                LastHeartbeat = rt.LastHeartbeat,
                GatewayVersion = rt.Version
            })
            .ToList();

    public async Task<CommandResult> SendCommandViaGatewayAsync(int gatewayId, string deviceIp, int? devicePort, string commandId, Dictionary<string, object>? parameters, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        if (!_gateways.TryGetValue(gatewayId, out var rt) || !IsGatewayOnline(gatewayId))
            return Failure(commandId, "Gateway offline", sw);

        var requestId = Guid.NewGuid().ToString("N");
        var cmd = new GatewayCommandMessage
        {
            RequestId = requestId,
            CommandId = commandId,
            DeviceIp = deviceIp,
            DevicePort = devicePort,
            Parameters = parameters
        };

        var resp = await SendAndAwaitAsync(rt.TenantId, gatewayId, requestId, cmd, cancellationToken);
        sw.Stop();

        if (resp is null)
            return Failure(commandId, "Gateway response timed out", sw);

        return new CommandResult
        {
            Success = resp.Success,
            CommandId = commandId,
            Response = resp.Response,
            ErrorMessage = resp.ErrorMessage,
            ResponseData = resp.ResponseData,
            RoutedVia = "mqtt-gateway",
            ElapsedMs = sw.ElapsedMilliseconds
        };
    }

    public async Task<DeviceState?> RequestDevicePollAsync(int gatewayId, string deviceIp, int? devicePort, string descriptorJson, CancellationToken cancellationToken = default)
    {
        if (!_gateways.TryGetValue(gatewayId, out var rt) || !IsGatewayOnline(gatewayId))
            return null;

        var requestId = Guid.NewGuid().ToString("N");
        var cmd = new GatewayCommandMessage
        {
            RequestId = requestId,
            CommandId = "poll",
            DeviceIp = deviceIp,
            DevicePort = devicePort,
            DescriptorJson = descriptorJson
        };

        var resp = await SendAndAwaitAsync(rt.TenantId, gatewayId, requestId, cmd, cancellationToken);
        if (resp is { Success: true } && !string.IsNullOrEmpty(resp.Response))
        {
            try { return JsonSerializer.Deserialize<DeviceState>(resp.Response); }
            catch (Exception ex) { _logger.LogWarning("Failed to parse polled DeviceState from gateway {GatewayId}: {Message}", gatewayId, ex.Message); }
        }
        return null;
    }

    public async Task<CommandResult> SendScriptViaGatewayAsync(int gatewayId, string scriptType, string scriptContent, string scriptHash, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        if (!_gateways.TryGetValue(gatewayId, out var rt) || !IsGatewayOnline(gatewayId))
            return Failure("run-script", "Gateway offline", sw);

        var requestId = Guid.NewGuid().ToString("N");
        var cmd = new GatewayCommandMessage
        {
            RequestId = requestId,
            CommandId = "run-script",
            ScriptType = scriptType,
            ScriptContent = scriptContent,
            ScriptHash = scriptHash,
            TimeoutMs = timeoutMs
        };

        // Wait for the reply a bit longer than the script's own timeout so the agent has time to finish.
        var waitMs = (timeoutMs ?? 60000) + 10000;
        var resp = await SendAndAwaitAsync(rt.TenantId, gatewayId, requestId, cmd, cancellationToken, waitMs);
        sw.Stop();

        if (resp is null)
            return Failure("run-script", "Gateway response timed out", sw);

        return new CommandResult
        {
            Success = resp.Success,
            CommandId = "run-script",
            Response = resp.Response,
            ErrorMessage = resp.ErrorMessage,
            RoutedVia = "mqtt-gateway",
            ElapsedMs = sw.ElapsedMilliseconds,
            ExitCode = resp.ExitCode,
            Stdout = resp.Stdout,
            Stderr = resp.Stderr
        };
    }

    private async Task<GatewayResponseMessage?> SendAndAwaitAsync(string tenantId, int gatewayId, string requestId, GatewayCommandMessage cmd, CancellationToken cancellationToken, int? waitTimeoutMs = null)
    {
        var tcs = new TaskCompletionSource<GatewayResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        try
        {
            var topic = SentinelTopics.GatewayCommand(tenantId, gatewayId.ToString());
            await _bus.PublishAsync(topic, SentinelJson.Serialize(cmd), retain: false, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(waitTimeoutMs ?? _options.CommandTimeoutSeconds * 1000));
            using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled());

            try { return await tcs.Task; }
            catch (OperationCanceledException) { return null; }
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    // ── WebSocket lifecycle — inert in MQTT mode ──

    public Task RegisterGatewayAsync(int gatewayId, WebSocket webSocket, GatewayConnectionInfo info, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("WebSocket gateway {GatewayId} attempted to connect while MQTT gateway transport is active; rejecting", gatewayId);
        return Task.CompletedTask; // returning immediately closes the socket in the middleware
    }

    public Task UnregisterGatewayAsync(int gatewayId) => Task.CompletedTask;

    // ── Helpers ──

    private static CommandResult Failure(string commandId, string error, Stopwatch sw) => new()
    {
        Success = false,
        CommandId = commandId,
        ErrorMessage = error,
        RoutedVia = "mqtt-gateway",
        ElapsedMs = sw.ElapsedMilliseconds
    };

    private static T? SafeDeserialize<T>(string json)
    {
        try { return SentinelJson.Deserialize<T>(json); }
        catch { return default; }
    }

    private sealed class GatewayRuntime
    {
        public int GatewayId { get; init; }
        public string TenantId { get; set; } = string.Empty;
        public string? Version { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public bool Offline { get; set; }
    }
}
