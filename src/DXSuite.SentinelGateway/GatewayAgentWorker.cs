using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DXSuite.Sentinel.Contracts;
using MQTTnet;
using MQTTnet.Protocol;

namespace DXSuite.SentinelGateway;

/// <summary>
/// The edge gateway agent. Connects outbound to the MQTT broker, announces itself with a
/// retained heartbeat on its gateway state topic, registers a Last Will so the cloud learns
/// of ungraceful drops within the keep-alive window, and services probe commands from the
/// cloud by running LAN-side network probes locally and replying on its response topic.
/// </summary>
public class GatewayAgentWorker : BackgroundService
{
    private readonly GatewayAgentOptions _opt;
    private readonly GatewayAgentState _state;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GatewayAgentWorker> _log;
    private IMqttClient? _client;

    public GatewayAgentWorker(GatewayAgentOptions opt, GatewayAgentState state, IHostEnvironment env, ILogger<GatewayAgentWorker> log)
    {
        _opt = opt;
        _state = state;
        _env = env;
        _log = log;
    }

    private string Tenant => _opt.TenantId;
    private string Gateway => _opt.GatewayId.ToString();
    private string SelfDevice => _opt.ResolvedSelfDeviceId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Enrollment gate: a fresh agent (GatewayId 0) with a token claims its identity before connecting.
        while (!stoppingToken.IsCancellationRequested && !_opt.IsEnrolled)
        {
            if (await TryEnrollAsync(stoppingToken))
                break;
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
        if (stoppingToken.IsCancellationRequested) return;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnCommandAsync;
        _client.ConnectedAsync += async _ =>
        {
            _state.Connected = true;
            _state.ConnectedSinceUtc ??= DateTime.UtcNow;
            _log.LogInformation("Sentinel agent connected to broker {Host}:{Port} as tenant={Tenant} gateway={Gateway} roles=[{Roles}]",
                _opt.BrokerHost, _opt.BrokerPort, Tenant, Gateway, string.Join(",", _opt.EffectiveRoles));
            await SubscribeAndAnnounceAsync();
        };
        _client.DisconnectedAsync += e =>
        {
            _state.Connected = false;
            _log.LogWarning("Sentinel agent disconnected: {Reason}", e.Reason);
            return Task.CompletedTask;
        };

        var options = BuildOptions();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                    await _client.ConnectAsync(options, stoppingToken);
                else
                {
                    await PublishHeartbeatAsync();
                    if (_opt.HasRole(GatewayAgentOptions.RoleSelfMonitor))
                        await PublishSelfTelemetryAsync(online: true, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning("Sentinel agent connect/heartbeat failed: {Message}", ex.Message); }

            try { await Task.Delay(TimeSpan.FromSeconds(_client.IsConnected ? _opt.HeartbeatSeconds : 5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        if (_client.IsConnected)
        {
            // Mark the host offline cleanly so the retained state isn't stale after a graceful stop.
            if (_opt.HasRole(GatewayAgentOptions.RoleSelfMonitor))
            {
                try { await PublishSelfTelemetryAsync(online: false, CancellationToken.None); } catch { /* shutting down */ }
            }
            try { await _client.DisconnectAsync(); } catch { /* shutting down */ }
        }
    }

    private async Task PublishSelfTelemetryAsync(bool online, CancellationToken ct)
    {
        var metrics = online ? await HostMetricsCollector.CollectAsync(ct) : null;
        var now = DateTime.UtcNow.ToString("o");

        // Retained state — late subscribers get the latest snapshot; offline flips it on graceful stop.
        var state = new DeviceStateMessage
        {
            TenantId = Tenant,
            DeviceId = SelfDevice,
            TimestampUtc = now,
            Online = online,
            GatewayId = _opt.GatewayId,
            StateJson = metrics is null ? null : SentinelJson.Serialize(metrics)
        };
        await _client!.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(SentinelTopics.DeviceState(Tenant, SelfDevice))
            .WithPayload(SentinelJson.Serialize(state))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(true)
            .Build());

        if (metrics is null) return;

        // Health beat — the time-series sample (not retained).
        var health = new DeviceHealthMessage { TenantId = Tenant, DeviceId = SelfDevice, TimestampUtc = now, GatewayId = _opt.GatewayId, Metrics = metrics };
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(SentinelTopics.DeviceHealth(Tenant, SelfDevice))
            .WithPayload(SentinelJson.Serialize(health))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());

        _state.LastTelemetryUtc = DateTime.UtcNow;
        _state.LastCpuPercent = metrics.CpuPercent;
        _state.LastMemoryUsedPercent = metrics.MemoryUsedPercent;
    }

    private async Task<bool> TryEnrollAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.EnrollmentToken) || string.IsNullOrWhiteSpace(_opt.EnrollUrl))
        {
            _log.LogWarning("Agent is not enrolled and no enrollment token/URL is configured; idling.");
            return false;
        }

        try
        {
            var url = _opt.EnrollUrl!.TrimEnd('/') + "/api/sentinel/gateway/enroll";
            var req = new GatewayEnrollRequest
            {
                Token = _opt.EnrollmentToken!,
                MachineFingerprint = MachineIdentity.GetFingerprint(),
                HostName = Environment.MachineName,
                Roles = _opt.Roles.Count > 0 ? string.Join(",", _opt.Roles) : null
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _log.LogInformation("Enrolling with {Url} as {Host}…", url, req.HostName);
            var httpResp = await http.PostAsJsonAsync(url, req, ct);
            var resp = await httpResp.Content.ReadFromJsonAsync<GatewayEnrollResponse>(cancellationToken: ct);

            if (resp is null || !resp.Success)
            {
                _log.LogWarning("Enrollment rejected: {Error}", resp?.ErrorMessage ?? httpResp.StatusCode.ToString());
                return false;
            }

            // Adopt the assigned identity.
            _opt.GatewayId = resp.GatewayId;
            if (!string.IsNullOrEmpty(resp.TenantId)) _opt.TenantId = resp.TenantId;
            _opt.Username = $"gw-{resp.GatewayId}";
            _opt.Password = resp.ApiKey;
            if (!string.IsNullOrEmpty(resp.BrokerHost)) _opt.BrokerHost = resp.BrokerHost;
            if (resp.BrokerPort > 0) _opt.BrokerPort = resp.BrokerPort;
            if (!string.IsNullOrWhiteSpace(resp.Roles))
                _opt.Roles = resp.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            _state.GatewayId = _opt.GatewayId;
            _state.TenantId = _opt.TenantId;
            _state.Roles = string.Join(", ", _opt.EffectiveRoles);
            _state.Broker = $"{_opt.BrokerHost}:{_opt.BrokerPort}";

            await PersistIdentityAsync();
            _log.LogInformation("Enrolled as gateway {GatewayId} (tenant {Tenant}, roles [{Roles}])",
                _opt.GatewayId, _opt.TenantId, string.Join(",", _opt.EffectiveRoles));
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _log.LogWarning("Enrollment attempt failed: {Message}", ex.Message);
            return false;
        }
    }

    private async Task PersistIdentityAsync()
    {
        try
        {
            var path = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var root = (File.Exists(path) ? JsonNode.Parse(await File.ReadAllTextAsync(path)) : null) as JsonObject ?? new JsonObject();
            var gw = root["Gateway"] as JsonObject ?? new JsonObject();

            gw["GatewayId"] = _opt.GatewayId;
            gw["TenantId"] = _opt.TenantId;
            gw["BrokerHost"] = _opt.BrokerHost;
            gw["BrokerPort"] = _opt.BrokerPort;
            gw["Username"] = _opt.Username;
            gw["Password"] = _opt.Password;
            var roles = new JsonArray();
            foreach (var r in _opt.EffectiveRoles) roles.Add(r);
            gw["Roles"] = roles;
            gw["EnrollmentToken"] = null; // one-time; clear once consumed

            root["Gateway"] = gw;
            await File.WriteAllTextAsync(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.LogWarning("Could not persist enrolled identity to appsettings.json: {Message}", ex.Message);
        }
    }

    private MqttClientOptions BuildOptions()
    {
        var will = SentinelJson.Serialize(new LastWillMessage
        {
            TenantId = Tenant,
            EntityType = "gw",
            EntityId = Gateway,
            Reason = "connection-lost"
        });

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_opt.BrokerHost, _opt.BrokerPort)
            .WithClientId($"sentinel-gw-{Tenant}-{Gateway}")
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithWillTopic(SentinelTopics.GatewayLwt(Tenant, Gateway))
            .WithWillPayload(Encoding.UTF8.GetBytes(will))
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(_opt.Username))
            builder = builder.WithCredentials(_opt.Username, _opt.Password);
        if (_opt.UseTls)
            builder = builder.WithTlsOptions(o => o.UseTls());

        return builder.Build();
    }

    private async Task SubscribeAndAnnounceAsync()
    {
        var sub = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(SentinelTopics.GatewayCommand(Tenant, Gateway)).WithAtLeastOnceQoS())
            .Build();
        await _client!.SubscribeAsync(sub);
        await PublishHeartbeatAsync();
    }

    private async Task PublishHeartbeatAsync()
    {
        var hb = SentinelJson.Serialize(new GatewayHeartbeatMessage
        {
            TenantId = Tenant,
            GatewayId = Gateway,
            Version = _opt.Version,
            TimestampUtc = DateTime.UtcNow.ToString("o")
        });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(SentinelTopics.GatewayState(Tenant, Gateway))
            .WithPayload(hb)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(true)
            .Build();

        await _client!.PublishAsync(message);
        _state.LastHeartbeatUtc = DateTime.UtcNow;
    }

    private async Task OnCommandAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var cmd = SafeDeserialize<GatewayCommandMessage>(e.ApplicationMessage.ConvertPayloadToString());
        if (cmd is null)
            return;

        _state.CommandsHandled++;
        _state.LastCommand = cmd.CommandId;
        _state.LastCommandUtc = DateTime.UtcNow;
        _log.LogInformation("Gateway command {CommandId} for {Ip}:{Port} (req {Req})",
            cmd.CommandId, cmd.DeviceIp, cmd.DevicePort, cmd.RequestId);

        var response = await RunCommandAsync(cmd);
        response.RequestId = cmd.RequestId;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(SentinelTopics.GatewayResponse(Tenant, Gateway))
            .WithPayload(SentinelJson.Serialize(response))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        if (_client is { IsConnected: true })
            await _client.PublishAsync(message);
    }

    private async Task<GatewayResponseMessage> RunCommandAsync(GatewayCommandMessage cmd)
    {
        // Task execution runs on this host, not against a LAN device — handle before the IP check.
        if (string.Equals(cmd.CommandId, "run-script", StringComparison.OrdinalIgnoreCase))
        {
            if (!_opt.HasRole(GatewayAgentOptions.RoleTasks))
                return new GatewayResponseMessage { Success = false, ErrorMessage = "Tasks role is not enabled on this agent" };
            return await ExecuteTaskAsync(cmd);
        }

        // Probing/bridging other LAN devices is the gateway role's job.
        if (!_opt.HasRole(GatewayAgentOptions.RoleGateway))
            return new GatewayResponseMessage { Success = false, ErrorMessage = $"Gateway role is not enabled on this agent (command '{cmd.CommandId}')" };

        if (string.IsNullOrEmpty(cmd.DeviceIp))
            return new GatewayResponseMessage { Success = false, ErrorMessage = "No device IP supplied" };

        var outcome = cmd.CommandId.ToLowerInvariant() switch
        {
            "ping" or "icmp-ping" or "reachability" or "poll" => await NetworkProbes.PingAsync(cmd.DeviceIp, _opt.ProbeTimeoutMs),
            "latency" => await NetworkProbes.LatencyAsync(cmd.DeviceIp, timeoutMs: _opt.ProbeTimeoutMs),
            "tcp-check" or "tcp-port" => await NetworkProbes.TcpCheckAsync(cmd.DeviceIp, ResolvePort(cmd), _opt.ProbeTimeoutMs),
            "wake-on-lan" => await RunWakeOnLanAsync(cmd),
            _ => new ProbeOutcome(false, "unsupported", $"Command '{cmd.CommandId}' is not supported by the gateway agent")
        };

        return new GatewayResponseMessage
        {
            Success = outcome.Success,
            Response = outcome.Detail,
            ResponseData = outcome.Data,
            ErrorMessage = outcome.Success ? null : outcome.Detail
        };
    }

    private async Task<GatewayResponseMessage> ExecuteTaskAsync(GatewayCommandMessage cmd)
    {
        if (string.IsNullOrEmpty(cmd.ScriptContent))
            return new GatewayResponseMessage { Success = false, ErrorMessage = "No script content supplied" };

        // Integrity gate: if a hash was provided, the content must match exactly before we run anything.
        if (!string.IsNullOrEmpty(cmd.ScriptHash))
        {
            if (!string.Equals(ComputeSha256(cmd.ScriptContent), cmd.ScriptHash, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Task rejected — script hash mismatch (req {Req})", cmd.RequestId);
                return new GatewayResponseMessage { Success = false, ErrorMessage = "Script hash mismatch — not executed" };
            }
        }
        else
        {
            _log.LogWarning("Task {Req} has no script hash — running unverified", cmd.RequestId);
        }

        var scriptType = string.IsNullOrEmpty(cmd.ScriptType) ? "powershell" : cmd.ScriptType;
        var timeout = cmd.TimeoutMs is > 0 ? cmd.TimeoutMs.Value : 60000;

        _log.LogInformation("Task starting: type={Type} timeout={Timeout}ms (req {Req})", scriptType, timeout, cmd.RequestId);
        var result = await TaskRunner.RunAsync(scriptType, cmd.ScriptContent, timeout, CancellationToken.None);
        _log.LogInformation("Task finished: success={Success} exit={Exit} (req {Req})", result.Success, result.ExitCode, cmd.RequestId);

        return new GatewayResponseMessage
        {
            Success = result.Success,
            ExitCode = result.ExitCode,
            Stdout = result.Stdout,
            Stderr = result.Stderr,
            Response = result.ExitCode is { } code ? $"exit {code}" : "no exit code",
            ErrorMessage = result.Error
        };
    }

    private static string ComputeSha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static async Task<ProbeOutcome> RunWakeOnLanAsync(GatewayCommandMessage cmd)
    {
        var mac = cmd.Parameters != null && cmd.Parameters.TryGetValue("mac", out var m) ? m?.ToString() : null;
        if (string.IsNullOrEmpty(mac))
            return new ProbeOutcome(false, "critical", "Wake-on-LAN requires a 'mac' parameter");
        return await NetworkProbes.WakeOnLanAsync(mac);
    }

    private static int ResolvePort(GatewayCommandMessage cmd)
    {
        if (cmd.DevicePort is > 0)
            return cmd.DevicePort.Value;
        if (cmd.Parameters != null && cmd.Parameters.TryGetValue("port", out var p) && int.TryParse(p?.ToString(), out var port))
            return port;
        return 80;
    }

    private static T? SafeDeserialize<T>(string json)
    {
        try { return SentinelJson.Deserialize<T>(json); }
        catch { return default; }
    }
}
