using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace DXSuite.Sentinel.Services.Mqtt;

/// <summary>
/// Owns the long-lived MQTT broker connection for the Sentinel control plane and
/// implements <see cref="ISentinelMessageBus"/>. Connects on startup, maintains the
/// session with automatic reconnect, re-applies subscriptions after every reconnect,
/// and routes incoming messages to registered handlers.
///
/// No-ops when Sentinel:Mqtt:Enabled is false so the application runs normally
/// without a broker present.
/// </summary>
public class MqttConnectionService : IHostedService, ISentinelMessageBus, IDisposable
{
    private readonly SentinelMqttOptions _options;
    private readonly ILogger<MqttConnectionService> _logger;

    private IMqttClient? _client;
    private MqttClientOptions? _clientOptions;
    private CancellationTokenSource? _cts;
    private Task? _connectionLoop;

    private readonly List<(string Filter, Func<SentinelMessage, Task> Handler)> _subscriptions = new();
    private readonly object _subLock = new();

    public MqttConnectionService(SentinelMqttOptions options, ILogger<MqttConnectionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsConnected => _client?.IsConnected ?? false;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Sentinel MQTT is disabled (Sentinel:Mqtt:Enabled=false)");
            return Task.CompletedTask;
        }

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(_options.Username))
            builder = builder.WithCredentials(_options.Username, _options.Password);

        if (_options.UseTls)
            builder = builder.WithTlsOptions(o => o.UseTls());

        _clientOptions = builder.Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.ConnectedAsync += async _ =>
        {
            _logger.LogInformation("Sentinel MQTT connected to {Host}:{Port}", _options.Host, _options.Port);
            await ResubscribeAllAsync();
        };
        _client.DisconnectedAsync += e =>
        {
            _logger.LogWarning("Sentinel MQTT disconnected: {Reason}", e.Reason);
            return Task.CompletedTask;
        };

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionLoop = Task.Run(() => MaintainConnectionAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default)
    {
        if (_client is not { IsConnected: true })
        {
            _logger.LogWarning("Sentinel MQTT publish to {Topic} skipped — not connected", topic);
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message, cancellationToken);
    }

    public async Task SubscribeAsync(string topicFilter, Func<SentinelMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        lock (_subLock)
        {
            _subscriptions.Add((topicFilter, handler));
        }

        // If already connected, apply immediately; otherwise ResubscribeAllAsync picks it up on connect.
        if (_client is { IsConnected: true })
            await SubscribeToBrokerAsync(topicFilter, cancellationToken);
    }

    private async Task SubscribeToBrokerAsync(string topicFilter, CancellationToken cancellationToken)
    {
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicFilter).WithAtLeastOnceQoS())
            .Build();
        await _client!.SubscribeAsync(options, cancellationToken);
        _logger.LogInformation("Sentinel MQTT subscribed to {Filter}", topicFilter);
    }

    private async Task ResubscribeAllAsync()
    {
        string[] filters;
        lock (_subLock)
        {
            filters = _subscriptions.Select(s => s.Filter).Distinct().ToArray();
        }

        foreach (var filter in filters)
        {
            try { await SubscribeToBrokerAsync(filter, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning("Sentinel MQTT re-subscribe to {Filter} failed: {Message}", filter, ex.Message); }
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

        (string Filter, Func<SentinelMessage, Task> Handler)[] matches;
        lock (_subLock)
        {
            matches = _subscriptions
                .Where(s => MqttTopicFilterComparer.Compare(topic, s.Filter) == MqttTopicFilterCompareResult.IsMatch)
                .ToArray();
        }

        var message = new SentinelMessage(topic, payload);
        foreach (var (_, handler) in matches)
        {
            try { await handler(message); }
            catch (Exception ex) { _logger.LogError(ex, "Sentinel MQTT handler failed for topic {Topic}", topic); }
        }
    }

    private async Task MaintainConnectionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_client is { IsConnected: false } && _clientOptions != null)
                    await _client.ConnectAsync(_clientOptions, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sentinel MQTT connect attempt failed: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(IsConnected ? 30 : 5), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_connectionLoop != null)
        {
            try { await _connectionLoop; }
            catch (OperationCanceledException) { }
        }

        if (_client is { IsConnected: true })
        {
            try { await _client.DisconnectAsync(); }
            catch (Exception ex) { _logger.LogDebug("Sentinel MQTT disconnect on shutdown failed: {Message}", ex.Message); }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _client?.Dispose();
    }
}
