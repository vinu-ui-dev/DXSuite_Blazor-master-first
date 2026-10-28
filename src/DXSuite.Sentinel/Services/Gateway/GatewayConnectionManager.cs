using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Gateway;

/// <summary>
/// Manages persistent WebSocket connections from Site Gateways.
/// Gateways initiate outbound WebSocket connections to Director (firewall-friendly).
/// This manager accepts those connections and provides a bidirectional command/event channel.
/// </summary>
public class GatewayConnectionManager : IGatewayConnectionManager, IDisposable
{
    private readonly ConcurrentDictionary<int, GatewayConnection> _connections = new();
    private readonly ILogger<GatewayConnectionManager> _logger;
    private readonly Timer _heartbeatMonitor;

    /// <summary>
    /// Gateways that miss heartbeats beyond this threshold are considered disconnected
    /// </summary>
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How often to check for stale gateway connections
    /// </summary>
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GatewayConnectionManager(ILogger<GatewayConnectionManager> logger)
    {
        _logger = logger;
        _heartbeatMonitor = new Timer(CheckHeartbeatTimeouts, null, MonitorInterval, MonitorInterval);
    }

    public bool IsGatewayOnline(int gatewayId)
    {
        if (!_connections.TryGetValue(gatewayId, out var conn))
            return false;

        // Gateway is online if the WebSocket is open and heartbeat is recent
        return conn.WebSocket.State == WebSocketState.Open &&
               DateTime.UtcNow - conn.Info.LastHeartbeat < HeartbeatTimeout;
    }

    public async Task<CommandResult> SendCommandViaGatewayAsync(
        int gatewayId,
        string deviceIp,
        int? devicePort,
        string commandId,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken = default)
    {
        var result = new CommandResult { CommandId = commandId, RoutedVia = "gateway" };

        if (!_connections.TryGetValue(gatewayId, out var conn))
        {
            result.ErrorMessage = $"Gateway {gatewayId} is not connected";
            return result;
        }

        if (conn.WebSocket.State != WebSocketState.Open)
        {
            result.ErrorMessage = $"Gateway {gatewayId} WebSocket is in state {conn.WebSocket.State}";
            return result;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var request = new GatewayMessage
        {
            Type = "command",
            RequestId = requestId,
            DeviceIp = deviceIp,
            DevicePort = devicePort,
            CommandId = commandId,
            Parameters = parameters
        };

        // Register a pending request with a completion source so we can await the response
        var tcs = new TaskCompletionSource<GatewayMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.PendingRequests[requestId] = tcs;

        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await conn.SendSemaphore.WaitAsync(cancellationToken);
            try
            {
                await conn.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
            finally
            {
                conn.SendSemaphore.Release();
            }

            _logger.LogDebug("Sent command {CommandId} (request {RequestId}) to gateway {GatewayId}",
                commandId, requestId, gatewayId);

            // Wait for the response with a timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var responseMessage = await tcs.Task.WaitAsync(cts.Token);

            result.Success = responseMessage.Success ?? false;
            result.Response = responseMessage.Response;
            result.ErrorMessage = responseMessage.ErrorMessage;
            result.ResponseData = responseMessage.ResponseData;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = $"Command {commandId} timed out waiting for gateway {gatewayId} response";
            _logger.LogWarning("Command {CommandId} timed out on gateway {GatewayId}", commandId, gatewayId);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Error sending command via gateway: {ex.Message}";
            _logger.LogError(ex, "Error sending command {CommandId} via gateway {GatewayId}", commandId, gatewayId);
        }
        finally
        {
            conn.PendingRequests.TryRemove(requestId, out _);
        }

        return result;
    }

    public Task<CommandResult> SendScriptViaGatewayAsync(int gatewayId, string scriptType, string scriptContent, string scriptHash, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        // Task/script execution is only supported over the MQTT gateway transport.
        return Task.FromResult(new CommandResult
        {
            Success = false,
            CommandId = "run-script",
            ErrorMessage = "Task execution requires the MQTT gateway transport (Sentinel:Mqtt:GatewayOverMqtt).",
            RoutedVia = "websocket-gateway"
        });
    }

    public async Task<DeviceState?> RequestDevicePollAsync(
        int gatewayId,
        string deviceIp,
        int? devicePort,
        string descriptorJson,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(gatewayId, out var conn))
            return null;

        if (conn.WebSocket.State != WebSocketState.Open)
            return null;

        var requestId = Guid.NewGuid().ToString("N");
        var request = new GatewayMessage
        {
            Type = "poll",
            RequestId = requestId,
            DeviceIp = deviceIp,
            DevicePort = devicePort,
            DescriptorJson = descriptorJson
        };

        var tcs = new TaskCompletionSource<GatewayMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.PendingRequests[requestId] = tcs;

        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await conn.SendSemaphore.WaitAsync(cancellationToken);
            try
            {
                await conn.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
            finally
            {
                conn.SendSemaphore.Release();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await tcs.Task.WaitAsync(cts.Token);

            if (response.Success == true && !string.IsNullOrEmpty(response.Response))
            {
                return JsonSerializer.Deserialize<DeviceState>(response.Response, JsonOptions);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Device poll request timed out on gateway {GatewayId} for {DeviceIp}", gatewayId, deviceIp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting device poll via gateway {GatewayId}", gatewayId);
        }
        finally
        {
            conn.PendingRequests.TryRemove(requestId, out _);
        }

        return null;
    }

    public IReadOnlyList<GatewayConnectionInfo> GetConnectedGateways()
    {
        return _connections.Values
            .Where(c => c.WebSocket.State == WebSocketState.Open)
            .Select(c => c.Info)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Register a newly connected gateway WebSocket. Called by the WebSocket middleware
    /// when a gateway establishes a connection.
    /// </summary>
    public async Task RegisterGatewayAsync(int gatewayId, WebSocket webSocket, GatewayConnectionInfo info, CancellationToken cancellationToken = default)
    {
        // Remove any existing stale connection for this gateway
        if (_connections.TryRemove(gatewayId, out var existing))
        {
            _logger.LogInformation("Replacing existing connection for gateway {GatewayId}", gatewayId);
            await CleanupConnectionAsync(existing);
        }

        var connection = new GatewayConnection
        {
            WebSocket = webSocket,
            Info = info
        };

        _connections[gatewayId] = connection;
        _logger.LogInformation(
            "Gateway {GatewayId} ({SiteName}) connected from {RemoteIp}",
            gatewayId, info.SiteName, info.RemoteIpAddress);

        // Start the receive loop on a background task
        _ = Task.Run(() => ReceiveLoopAsync(gatewayId, connection, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Remove a disconnected gateway
    /// </summary>
    public async Task UnregisterGatewayAsync(int gatewayId)
    {
        if (_connections.TryRemove(gatewayId, out var conn))
        {
            _logger.LogInformation("Gateway {GatewayId} ({SiteName}) disconnected", gatewayId, conn.Info.SiteName);
            await CleanupConnectionAsync(conn);
        }
    }

    /// <summary>
    /// Update the heartbeat timestamp for a gateway, keeping it marked as alive
    /// </summary>
    public void RecordHeartbeat(int gatewayId)
    {
        if (_connections.TryGetValue(gatewayId, out var conn))
        {
            conn.Info.LastHeartbeat = DateTime.UtcNow;
        }
    }

    private async Task ReceiveLoopAsync(int gatewayId, GatewayConnection connection, CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (connection.WebSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var messageBuilder = new StringBuilder();
                WebSocketReceiveResult receiveResult;

                // Read the complete message (may span multiple frames)
                do
                {
                    receiveResult = await connection.WebSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Gateway {GatewayId} sent close frame", gatewayId);
                        await UnregisterGatewayAsync(gatewayId);
                        return;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, receiveResult.Count));
                }
                while (!receiveResult.EndOfMessage);

                var messageJson = messageBuilder.ToString();

                try
                {
                    var message = JsonSerializer.Deserialize<GatewayMessage>(messageJson, JsonOptions);
                    if (message != null)
                    {
                        HandleIncomingMessage(gatewayId, connection, message);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize message from gateway {GatewayId}", gatewayId);
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("Gateway {GatewayId} connection closed prematurely", gatewayId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Receive loop cancelled for gateway {GatewayId}", gatewayId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive loop for gateway {GatewayId}", gatewayId);
        }
        finally
        {
            await UnregisterGatewayAsync(gatewayId);
        }
    }

    private void HandleIncomingMessage(int gatewayId, GatewayConnection connection, GatewayMessage message)
    {
        switch (message.Type?.ToLowerInvariant())
        {
            case "heartbeat":
                connection.Info.LastHeartbeat = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(message.GatewayVersion))
                {
                    connection.Info.GatewayVersion = message.GatewayVersion;
                }
                _logger.LogTrace("Heartbeat received from gateway {GatewayId}", gatewayId);
                break;

            case "response":
                // Match response to a pending request using the correlation ID
                if (!string.IsNullOrEmpty(message.RequestId) &&
                    connection.PendingRequests.TryRemove(message.RequestId, out var tcs))
                {
                    tcs.TrySetResult(message);
                    _logger.LogDebug("Response received for request {RequestId} from gateway {GatewayId}",
                        message.RequestId, gatewayId);
                }
                else
                {
                    _logger.LogWarning(
                        "Received response with unknown RequestId {RequestId} from gateway {GatewayId}",
                        message.RequestId, gatewayId);
                }
                break;

            case "event":
                // Gateway is forwarding a device event (state change, alert, etc.)
                _logger.LogInformation(
                    "Event received from gateway {GatewayId}: device={DeviceIp}, event={EventType}",
                    gatewayId, message.DeviceIp, message.CommandId);
                // Events are handled by the Sentinel engine via a separate event bus (future integration)
                break;

            default:
                _logger.LogWarning("Unknown message type '{Type}' from gateway {GatewayId}", message.Type, gatewayId);
                break;
        }
    }

    private void CheckHeartbeatTimeouts(object? state)
    {
        var now = DateTime.UtcNow;
        var staleGateways = _connections
            .Where(kvp => now - kvp.Value.Info.LastHeartbeat > HeartbeatTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var gatewayId in staleGateways)
        {
            _logger.LogWarning(
                "Gateway {GatewayId} heartbeat timed out (last heartbeat: {LastHeartbeat})",
                gatewayId, _connections.TryGetValue(gatewayId, out var c) ? c.Info.LastHeartbeat : DateTime.MinValue);

            // Schedule cleanup without blocking the timer callback
            _ = Task.Run(() => UnregisterGatewayAsync(gatewayId));
        }
    }

    private static async Task CleanupConnectionAsync(GatewayConnection connection)
    {
        // Cancel all pending requests
        foreach (var pending in connection.PendingRequests)
        {
            pending.Value.TrySetCanceled();
        }
        connection.PendingRequests.Clear();

        // Close the WebSocket gracefully if still open
        if (connection.WebSocket.State == WebSocketState.Open ||
            connection.WebSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await connection.WebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Gateway unregistered",
                    cts.Token);
            }
            catch
            {
                // Best-effort close; ignore errors during cleanup
            }
        }

        connection.SendSemaphore.Dispose();
        connection.WebSocket.Dispose();
    }

    public void Dispose()
    {
        _heartbeatMonitor.Dispose();

        foreach (var conn in _connections.Values)
        {
            foreach (var pending in conn.PendingRequests)
            {
                pending.Value.TrySetCanceled();
            }
            conn.PendingRequests.Clear();

            try
            {
                conn.WebSocket.Dispose();
            }
            catch { /* ignore cleanup errors */ }

            conn.SendSemaphore.Dispose();
        }

        _connections.Clear();
    }

    /// <summary>
    /// Holds a single gateway's WebSocket connection state, pending request tracking,
    /// and a semaphore to serialize outbound writes (WebSocket is not thread-safe for sends)
    /// </summary>
    private class GatewayConnection
    {
        public WebSocket WebSocket { get; set; } = null!;
        public GatewayConnectionInfo Info { get; set; } = new();
        public ConcurrentDictionary<string, TaskCompletionSource<GatewayMessage>> PendingRequests { get; } = new();
        public SemaphoreSlim SendSemaphore { get; } = new(1, 1);
    }

    /// <summary>
    /// Wire format for messages exchanged between Director and Site Gateways.
    /// Uses request/response correlation via RequestId.
    /// </summary>
    private class GatewayMessage
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; set; }

        [JsonPropertyName("deviceIp")]
        public string? DeviceIp { get; set; }

        [JsonPropertyName("devicePort")]
        public int? DevicePort { get; set; }

        [JsonPropertyName("commandId")]
        public string? CommandId { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, object>? Parameters { get; set; }

        [JsonPropertyName("descriptorJson")]
        public string? DescriptorJson { get; set; }

        [JsonPropertyName("success")]
        public bool? Success { get; set; }

        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("responseData")]
        public Dictionary<string, object>? ResponseData { get; set; }

        [JsonPropertyName("gatewayVersion")]
        public string? GatewayVersion { get; set; }
    }
}
