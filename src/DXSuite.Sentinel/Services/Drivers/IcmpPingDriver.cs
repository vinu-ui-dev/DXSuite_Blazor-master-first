using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Drivers;

/// <summary>
/// Driver for generic IP devices using ICMP ping for reachability checking
/// and optional HTTP health endpoint monitoring.
/// </summary>
public class IcmpPingDriver : IDeviceDriver
{
    public string ProtocolId => "icmp";

    private readonly ILogger<IcmpPingDriver> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Default ICMP timeout in milliseconds
    /// </summary>
    private const int DefaultPingTimeoutMs = 3000;

    /// <summary>
    /// Default HTTP health check timeout
    /// </summary>
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(5);

    public IcmpPingDriver(IHttpClientFactory httpClientFactory, ILogger<IcmpPingDriver> logger)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("IcmpPingDriver");
        _httpClient.Timeout = DefaultHttpTimeout;
    }

    public async Task<DeviceState> PollAsync(DeviceInstanceInfo device, DeviceDescriptorSchema descriptor, CancellationToken cancellationToken = default)
    {
        var state = new DeviceState
        {
            DeviceInstanceId = device.DeviceInstanceId,
            DeviceType = descriptor.DeviceType,
            DisplayName = device.DisplayName,
            IpAddress = device.IpAddress,
            StateTimestamp = DateTime.UtcNow,
            DataSource = new DataSourceInfo { HasGateway = true, PrimarySource = "icmp-poll" }
        };

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            state.OverallHealth = OverallHealthStatus.Unknown;
            return state;
        }

        foreach (var mp in descriptor.MonitorPoints)
        {
            try
            {
                var reading = await PollMonitorPointAsync(device, mp, cancellationToken);
                state.MonitorReadings[mp.Id] = reading;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll monitor point {MonitorPointId} for device {DeviceId}", mp.Id, device.DeviceInstanceId);
                state.MonitorReadings[mp.Id] = new MonitorReading
                {
                    MonitorPointId = mp.Id,
                    Name = mp.Name,
                    Status = "unknown",
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        state.OverallHealth = DetermineOverallHealth(state.MonitorReadings.Values);
        state.LastHeartbeat = DateTime.UtcNow;

        return state;
    }

    public async Task<CommandResult> ExecuteCommandAsync(DeviceInstanceInfo device, string commandId, Dictionary<string, object>? parameters, DeviceDescriptorSchema descriptor, CancellationToken cancellationToken = default)
    {
        var result = new CommandResult { CommandId = commandId, RoutedVia = "direct" };
        var sw = Stopwatch.StartNew();

        try
        {
            switch (commandId.ToLowerInvariant())
            {
                case "wake-on-lan":
                    result = await ExecuteWakeOnLanAsync(device, result);
                    break;

                case "ping":
                    if (string.IsNullOrEmpty(device.IpAddress))
                    {
                        result.ErrorMessage = "Device IP address not configured";
                        break;
                    }
                    var pingReply = await SendPingAsync(device.IpAddress, DefaultPingTimeoutMs);
                    result.Success = pingReply.Status == IPStatus.Success;
                    result.Response = $"Ping {(result.Success ? "succeeded" : "failed")}: " +
                                     $"Status={pingReply.Status}, RTT={pingReply.RoundtripTime}ms";
                    result.ResponseData = new Dictionary<string, object>
                    {
                        ["status"] = pingReply.Status.ToString(),
                        ["roundtripMs"] = pingReply.RoundtripTime
                    };
                    break;

                default:
                    result.ErrorMessage = $"Command '{commandId}' is not supported by the ICMP driver. " +
                                          "Supported commands: wake-on-lan, ping";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error executing command {CommandId} on device {DeviceId}", commandId, device.DeviceInstanceId);
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(DeviceInstanceInfo device, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(device.IpAddress))
            return false;

        try
        {
            var reply = await SendPingAsync(device.IpAddress, DefaultPingTimeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ping test failed for {IpAddress}", device.IpAddress);
            return false;
        }
    }

    private async Task<MonitorReading> PollMonitorPointAsync(DeviceInstanceInfo device, MonitorPointDefinition mp, CancellationToken ct)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = mp.Id,
            Name = mp.Name,
            Timestamp = DateTime.UtcNow
        };

        switch (mp.Id.ToLowerInvariant())
        {
            case "reachability":
            case "ping":
                await PollPingAsync(device.IpAddress!, reading, mp);
                break;

            case "latency":
                await PollLatencyAsync(device.IpAddress!, reading, mp);
                break;

            case "http-health":
                await PollHttpHealthAsync(device, reading, mp, ct);
                break;

            case "tcp-port":
                await PollTcpPortAsync(device, reading, mp, ct);
                break;

            default:
                // Default to ICMP ping for unknown monitor points
                if (!string.IsNullOrEmpty(device.IpAddress))
                {
                    await PollPingAsync(device.IpAddress, reading, mp);
                }
                else
                {
                    reading.Status = "unknown";
                }
                break;
        }

        return reading;
    }

    private async Task PollPingAsync(string ipAddress, MonitorReading reading, MonitorPointDefinition mp)
    {
        try
        {
            var reply = await SendPingAsync(ipAddress, DefaultPingTimeoutMs);
            var isReachable = reply.Status == IPStatus.Success;
            reading.Value = isReachable ? "reachable" : "unreachable";
            reading.DisplayValue = isReachable ? $"Reachable ({reply.RoundtripTime}ms)" : $"Unreachable ({reply.Status})";
            reading.Status = isReachable ? "healthy" : "critical";
        }
        catch (PingException ex)
        {
            reading.Value = "error";
            reading.DisplayValue = $"Ping error: {ex.Message}";
            reading.Status = "critical";
        }
    }

    private async Task PollLatencyAsync(string ipAddress, MonitorReading reading, MonitorPointDefinition mp)
    {
        try
        {
            // Send multiple pings to get an average round-trip time
            const int pingCount = 3;
            var roundTrips = new List<long>();

            for (int i = 0; i < pingCount; i++)
            {
                var reply = await SendPingAsync(ipAddress, DefaultPingTimeoutMs);
                if (reply.Status == IPStatus.Success)
                {
                    roundTrips.Add(reply.RoundtripTime);
                }
            }

            if (roundTrips.Count > 0)
            {
                var avgMs = (int)roundTrips.Average();
                reading.Value = avgMs;
                reading.DisplayValue = $"{avgMs}ms avg ({roundTrips.Count}/{pingCount} replies)";
                reading.Status = EvaluateLatencyThreshold(avgMs, mp);
            }
            else
            {
                reading.Value = -1;
                reading.DisplayValue = "No response";
                reading.Status = "critical";
            }
        }
        catch (PingException ex)
        {
            reading.Value = -1;
            reading.DisplayValue = $"Ping error: {ex.Message}";
            reading.Status = "critical";
        }
    }

    private async Task PollHttpHealthAsync(DeviceInstanceInfo device, MonitorReading reading, MonitorPointDefinition mp, CancellationToken ct)
    {
        // Build the health endpoint URL from config or descriptor parameters
        var healthUrl = mp.RetrieveParams?.TryGetValue("url", out var urlObj) == true
            ? urlObj?.ToString()
            : null;

        if (string.IsNullOrEmpty(healthUrl))
        {
            // Fall back to constructing from IP and optional port/path
            var port = device.Port ?? (mp.RetrieveParams?.TryGetValue("port", out var portObj) == true ? Convert.ToInt32(portObj) : 80);
            var path = mp.RetrieveParams?.TryGetValue("path", out var pathObj) == true ? pathObj?.ToString() : "/health";
            var scheme = port == 443 ? "https" : "http";
            healthUrl = $"{scheme}://{device.IpAddress}:{port}{path}";
        }

        try
        {
            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.GetAsync(healthUrl, ct);
            sw.Stop();

            var statusCode = (int)response.StatusCode;
            var isHealthy = response.IsSuccessStatusCode;

            reading.Value = statusCode;
            reading.DisplayValue = $"HTTP {statusCode} ({sw.ElapsedMilliseconds}ms)";
            reading.Status = isHealthy ? "healthy" : "critical";
        }
        catch (TaskCanceledException)
        {
            reading.Value = "timeout";
            reading.DisplayValue = "HTTP request timed out";
            reading.Status = "critical";
        }
        catch (HttpRequestException ex)
        {
            reading.Value = "error";
            reading.DisplayValue = $"HTTP error: {ex.Message}";
            reading.Status = "critical";
        }
    }

    private async Task PollTcpPortAsync(DeviceInstanceInfo device, MonitorReading reading, MonitorPointDefinition mp, CancellationToken ct)
    {
        var port = mp.RetrieveParams?.TryGetValue("port", out var portObj) == true
            ? Convert.ToInt32(portObj)
            : device.Port ?? 80;

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            reading.Status = "unknown";
            reading.DisplayValue = "No IP address";
            return;
        }

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(device.IpAddress, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(DefaultPingTimeoutMs, ct)) == connectTask;

            if (completed && client.Connected)
            {
                reading.Value = "open";
                reading.DisplayValue = $"Port {port} open";
                reading.Status = "healthy";
            }
            else
            {
                reading.Value = "closed";
                reading.DisplayValue = $"Port {port} closed/timeout";
                reading.Status = "critical";
            }
        }
        catch (SocketException)
        {
            reading.Value = "closed";
            reading.DisplayValue = $"Port {port} refused";
            reading.Status = "critical";
        }
    }

    private async Task<CommandResult> ExecuteWakeOnLanAsync(DeviceInstanceInfo device, CommandResult result)
    {
        if (string.IsNullOrEmpty(device.MacAddress))
        {
            result.ErrorMessage = "Wake-on-LAN requires a MAC address, but none is configured for this device";
            return result;
        }

        try
        {
            var macBytes = ParseMacAddress(device.MacAddress);

            // Build the Wake-on-LAN magic packet: 6 bytes of 0xFF followed by the MAC address repeated 16 times
            var magicPacket = new byte[6 + (6 * 16)];
            for (int i = 0; i < 6; i++)
                magicPacket[i] = 0xFF;
            for (int i = 0; i < 16; i++)
                Array.Copy(macBytes, 0, magicPacket, 6 + (i * 6), 6);

            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            await udpClient.SendAsync(magicPacket, magicPacket.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 9));

            result.Success = true;
            result.Response = $"Wake-on-LAN magic packet sent to {device.MacAddress}";
            _logger.LogInformation("Sent Wake-on-LAN packet to {MacAddress} for device {DeviceId}", device.MacAddress, device.DeviceInstanceId);
        }
        catch (FormatException ex)
        {
            result.ErrorMessage = $"Invalid MAC address format '{device.MacAddress}': {ex.Message}";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to send Wake-on-LAN packet: {ex.Message}";
            _logger.LogError(ex, "Wake-on-LAN failed for device {DeviceId}", device.DeviceInstanceId);
        }

        return result;
    }

    private static byte[] ParseMacAddress(string mac)
    {
        // Accept common MAC formats: AA:BB:CC:DD:EE:FF, AA-BB-CC-DD-EE-FF, AABBCCDDEEFF
        var cleaned = mac.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
        if (cleaned.Length != 12)
            throw new FormatException($"MAC address must be 12 hex characters, got {cleaned.Length}");

        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static async Task<PingReply> SendPingAsync(string ipAddress, int timeoutMs)
    {
        using var ping = new Ping();
        return await ping.SendPingAsync(ipAddress, timeoutMs);
    }

    private static string EvaluateLatencyThreshold(int valueMs, MonitorPointDefinition mp)
    {
        if (mp.Thresholds?.Critical != null)
        {
            var criticalVal = mp.Thresholds.Critical.Value?.ToString();
            if (double.TryParse(criticalVal, out var critThreshold))
            {
                var isCritical = mp.Thresholds.Critical.Operator.ToLowerInvariant() switch
                {
                    ">" => valueMs > critThreshold,
                    ">=" => valueMs >= critThreshold,
                    _ => false
                };
                if (isCritical) return "critical";
            }
        }

        if (mp.Thresholds?.Warning != null)
        {
            var warningVal = mp.Thresholds.Warning.Value?.ToString();
            if (double.TryParse(warningVal, out var warnThreshold))
            {
                var isWarning = mp.Thresholds.Warning.Operator.ToLowerInvariant() switch
                {
                    ">" => valueMs > warnThreshold,
                    ">=" => valueMs >= warnThreshold,
                    _ => false
                };
                if (isWarning) return "warning";
            }
        }

        return "healthy";
    }

    private static OverallHealthStatus DetermineOverallHealth(IEnumerable<MonitorReading> readings)
    {
        var readingsList = readings.ToList();
        if (readingsList.Count == 0) return OverallHealthStatus.Unknown;
        if (readingsList.Any(r => r.Status == "critical")) return OverallHealthStatus.Critical;
        if (readingsList.Any(r => r.Status == "warning")) return OverallHealthStatus.Warning;
        if (readingsList.All(r => r.Status == "healthy")) return OverallHealthStatus.Healthy;
        return OverallHealthStatus.Unknown;
    }
}
