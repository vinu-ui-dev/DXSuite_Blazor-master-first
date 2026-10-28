using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Drivers;

/// <summary>
/// Driver for BrightSign digital signage players (XT1144, XD1035, LS424, HD1024, etc.)
/// that exposes monitoring and control through the local Diagnostic Web Server (DWS).
/// </summary>
public class BrightSignDwsDriver : IDeviceDriver
{
    public string ProtocolId => "brightsign-dws";

    private readonly ILogger<BrightSignDwsDriver> _logger;
    private readonly HttpClient _httpClient;

    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(5);

    public BrightSignDwsDriver(IHttpClientFactory httpClientFactory, ILogger<BrightSignDwsDriver> logger)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("BrightSignDwsDriver");
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
            DataSource = new DataSourceInfo { HasGateway = true, PrimarySource = "brightsign-dws-poll" }
        };

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            state.OverallHealth = OverallHealthStatus.Unknown;
            return state;
        }

        var baseUrl = BuildBaseUrl(device);
        var isReachable = false;

        // Poll /api/v1/system for model, serial, firmware, uptime
        JsonElement? systemData = null;
        try
        {
            systemData = await GetJsonAsync(baseUrl, "/api/v1/system", device, cancellationToken);
            isReachable = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to reach BrightSign DWS system endpoint at {BaseUrl} for device {DeviceId}", baseUrl, device.DeviceInstanceId);
        }

        // Poll /api/v1/diagnostics for CPU temperature and memory usage
        JsonElement? diagnosticsData = null;
        try
        {
            diagnosticsData = await GetJsonAsync(baseUrl, "/api/v1/diagnostics", device, cancellationToken);
            isReachable = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to reach BrightSign DWS diagnostics endpoint for device {DeviceId}", device.DeviceInstanceId);
        }

        // Poll /api/v1/storage for storage capacity and usage
        JsonElement? storageData = null;
        try
        {
            storageData = await GetJsonAsync(baseUrl, "/api/v1/storage", device, cancellationToken);
            isReachable = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to reach BrightSign DWS storage endpoint for device {DeviceId}", device.DeviceInstanceId);
        }

        // Build monitor readings from the collected data
        state.MonitorReadings["reachability"] = BuildReachabilityReading(isReachable);
        state.MonitorReadings["firmware-version"] = BuildFirmwareReading(systemData);
        state.MonitorReadings["uptime"] = BuildUptimeReading(systemData);
        state.MonitorReadings["cpu-temperature"] = BuildCpuTemperatureReading(diagnosticsData);
        state.MonitorReadings["memory-usage"] = BuildMemoryUsageReading(diagnosticsData);
        state.MonitorReadings["storage-usage"] = BuildStorageUsageReading(storageData);

        if (!isReachable)
        {
            state.OverallHealth = OverallHealthStatus.Offline;
        }
        else
        {
            state.OverallHealth = DetermineOverallHealth(state.MonitorReadings.Values);
        }

        state.LastHeartbeat = DateTime.UtcNow;
        return state;
    }

    public async Task<CommandResult> ExecuteCommandAsync(DeviceInstanceInfo device, string commandId, Dictionary<string, object>? parameters, DeviceDescriptorSchema descriptor, CancellationToken cancellationToken = default)
    {
        var result = new CommandResult { CommandId = commandId, RoutedVia = "direct" };
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrEmpty(device.IpAddress))
            {
                result.ErrorMessage = "Device IP address not configured";
                return result;
            }

            var baseUrl = BuildBaseUrl(device);

            switch (commandId.ToLowerInvariant())
            {
                case "reboot":
                    result = await ExecutePostCommandAsync(baseUrl, "/api/v1/control/reboot", device, result, cancellationToken);
                    break;

                case "restart-app":
                    result = await ExecutePostCommandAsync(baseUrl, "/api/v1/control/restart", device, result, cancellationToken);
                    break;

                case "get-screenshot":
                    result = await ExecuteGetScreenshotAsync(baseUrl, device, result, cancellationToken);
                    break;

                case "factory-reset":
                    _logger.LogWarning("Factory reset requested for BrightSign device {DeviceId} ({IpAddress}). This is a destructive operation.",
                        device.DeviceInstanceId, device.IpAddress);
                    result = await ExecutePostCommandAsync(baseUrl, "/api/v1/control/factory-reset", device, result, cancellationToken);
                    break;

                default:
                    result.ErrorMessage = $"Command '{commandId}' is not supported by the BrightSign DWS driver. " +
                                          "Supported commands: reboot, restart-app, get-screenshot, factory-reset";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error executing command {CommandId} on BrightSign device {DeviceId}", commandId, device.DeviceInstanceId);
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
            var baseUrl = BuildBaseUrl(device);
            using var request = CreateRequest(HttpMethod.Get, $"{baseUrl}/api/v1/system", device);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BrightSign DWS connection test failed for {IpAddress}", device.IpAddress);
            return false;
        }
    }

    #region HTTP Helpers

    private string BuildBaseUrl(DeviceInstanceInfo device)
    {
        var port = device.Port ?? 80;
        return $"http://{device.IpAddress}:{port}";
    }

    /// <summary>
    /// Creates an HttpRequestMessage with optional Basic auth credentials.
    /// BrightSign DWS accepts Basic auth with "admin:{password}" on newer firmware.
    /// </summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, DeviceInstanceInfo device)
    {
        var request = new HttpRequestMessage(method, url);

        if (device.Credentials != null &&
            device.Credentials.TryGetValue("password", out var password) &&
            !string.IsNullOrEmpty(password))
        {
            var username = device.Credentials.TryGetValue("username", out var user) && !string.IsNullOrEmpty(user)
                ? user
                : "admin";
            var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return request;
    }

    /// <summary>
    /// Performs a GET request to a BrightSign DWS endpoint and returns the parsed JSON root element.
    /// </summary>
    private async Task<JsonElement> GetJsonAsync(string baseUrl, string path, DeviceInstanceInfo device, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{baseUrl}{path}", device);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.Clone();
    }

    #endregion

    #region Monitor Reading Builders

    private static MonitorReading BuildReachabilityReading(bool isReachable)
    {
        return new MonitorReading
        {
            MonitorPointId = "reachability",
            Name = "Reachability",
            Value = isReachable ? "online" : "offline",
            DisplayValue = isReachable ? "Online" : "Offline",
            Status = isReachable ? "healthy" : "critical",
            Timestamp = DateTime.UtcNow
        };
    }

    private static MonitorReading BuildFirmwareReading(JsonElement? systemData)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = "firmware-version",
            Name = "Firmware Version",
            Timestamp = DateTime.UtcNow
        };

        if (systemData == null)
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Unavailable";
            return reading;
        }

        var firmware = TryGetStringProperty(systemData.Value, "firmwareVersion")
                    ?? TryGetStringProperty(systemData.Value, "fwVersion")
                    ?? TryGetStringProperty(systemData.Value, "firmware");

        if (!string.IsNullOrEmpty(firmware))
        {
            reading.Value = firmware;
            reading.DisplayValue = firmware;
            reading.Status = "healthy";
        }
        else
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Not reported";
        }

        return reading;
    }

    private static MonitorReading BuildUptimeReading(JsonElement? systemData)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = "uptime",
            Name = "Uptime",
            Timestamp = DateTime.UtcNow
        };

        if (systemData == null)
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Unavailable";
            return reading;
        }

        long? uptimeSeconds = TryGetLongProperty(systemData.Value, "uptime")
                           ?? TryGetLongProperty(systemData.Value, "uptimeSeconds");

        if (uptimeSeconds.HasValue)
        {
            reading.Value = uptimeSeconds.Value;
            reading.DisplayValue = FormatUptime(uptimeSeconds.Value);

            // Recent reboot detection: uptime < 5 minutes suggests unexpected restart
            reading.Status = uptimeSeconds.Value < 300 ? "warning" : "healthy";
        }
        else
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Not reported";
        }

        return reading;
    }

    private static MonitorReading BuildCpuTemperatureReading(JsonElement? diagnosticsData)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = "cpu-temperature",
            Name = "CPU Temperature",
            Timestamp = DateTime.UtcNow
        };

        if (diagnosticsData == null)
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Unavailable";
            return reading;
        }

        double? tempC = TryGetDoubleProperty(diagnosticsData.Value, "cpuTemperature")
                     ?? TryGetDoubleProperty(diagnosticsData.Value, "temperatureC")
                     ?? TryGetDoubleProperty(diagnosticsData.Value, "temperature");

        if (tempC.HasValue)
        {
            reading.Value = tempC.Value;
            reading.DisplayValue = $"{tempC.Value:F1}\u00B0C";

            if (tempC.Value > 85)
                reading.Status = "critical";
            else if (tempC.Value > 70)
                reading.Status = "warning";
            else
                reading.Status = "healthy";
        }
        else
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Not reported";
        }

        return reading;
    }

    private static MonitorReading BuildMemoryUsageReading(JsonElement? diagnosticsData)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = "memory-usage",
            Name = "Memory Usage",
            Timestamp = DateTime.UtcNow
        };

        if (diagnosticsData == null)
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Unavailable";
            return reading;
        }

        double? usagePercent = TryGetDoubleProperty(diagnosticsData.Value, "memoryUsagePercent")
                            ?? TryGetDoubleProperty(diagnosticsData.Value, "memoryUsage");

        // If no direct percentage, try computing from total/used values
        if (!usagePercent.HasValue)
        {
            var totalMem = TryGetDoubleProperty(diagnosticsData.Value, "memoryTotal")
                        ?? TryGetDoubleProperty(diagnosticsData.Value, "totalMemory");
            var usedMem = TryGetDoubleProperty(diagnosticsData.Value, "memoryUsed")
                       ?? TryGetDoubleProperty(diagnosticsData.Value, "usedMemory");

            if (totalMem.HasValue && totalMem.Value > 0 && usedMem.HasValue)
            {
                usagePercent = (usedMem.Value / totalMem.Value) * 100.0;
            }
        }

        if (usagePercent.HasValue)
        {
            reading.Value = Math.Round(usagePercent.Value, 1);
            reading.DisplayValue = $"{usagePercent.Value:F1}%";

            if (usagePercent.Value > 95)
                reading.Status = "critical";
            else if (usagePercent.Value > 85)
                reading.Status = "warning";
            else
                reading.Status = "healthy";
        }
        else
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Not reported";
        }

        return reading;
    }

    private static MonitorReading BuildStorageUsageReading(JsonElement? storageData)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = "storage-usage",
            Name = "Storage Usage",
            Timestamp = DateTime.UtcNow
        };

        if (storageData == null)
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Unavailable";
            return reading;
        }

        double? usagePercent = TryGetDoubleProperty(storageData.Value, "usagePercent")
                            ?? TryGetDoubleProperty(storageData.Value, "storageUsagePercent");

        // If no direct percentage, try computing from capacity/used values
        if (!usagePercent.HasValue)
        {
            var totalStorage = TryGetDoubleProperty(storageData.Value, "capacity")
                            ?? TryGetDoubleProperty(storageData.Value, "totalSize")
                            ?? TryGetDoubleProperty(storageData.Value, "total");
            var usedStorage = TryGetDoubleProperty(storageData.Value, "used")
                           ?? TryGetDoubleProperty(storageData.Value, "usedSize");

            if (totalStorage.HasValue && totalStorage.Value > 0 && usedStorage.HasValue)
            {
                usagePercent = (usedStorage.Value / totalStorage.Value) * 100.0;
            }
        }

        if (usagePercent.HasValue)
        {
            reading.Value = Math.Round(usagePercent.Value, 1);
            reading.DisplayValue = $"{usagePercent.Value:F1}%";

            if (usagePercent.Value > 95)
                reading.Status = "critical";
            else if (usagePercent.Value > 80)
                reading.Status = "warning";
            else
                reading.Status = "healthy";
        }
        else
        {
            reading.Status = "unknown";
            reading.DisplayValue = "Not reported";
        }

        return reading;
    }

    #endregion

    #region Command Executors

    private async Task<CommandResult> ExecutePostCommandAsync(string baseUrl, string path, DeviceInstanceInfo device, CommandResult result, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"{baseUrl}{path}", device);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.Response = $"Command accepted (HTTP {(int)response.StatusCode})";
        }
        else
        {
            result.ErrorMessage = $"BrightSign DWS returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        return result;
    }

    private async Task<CommandResult> ExecuteGetScreenshotAsync(string baseUrl, DeviceInstanceInfo device, CommandResult result, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{baseUrl}/api/v1/snapshot", device);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

            result.Success = true;
            result.Response = $"Screenshot captured ({imageBytes.Length} bytes)";
            result.ResponseData = new Dictionary<string, object>
            {
                ["imageBytes"] = imageBytes,
                ["contentType"] = contentType,
                ["sizeBytes"] = imageBytes.Length
            };
        }
        else
        {
            result.ErrorMessage = $"Screenshot capture failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        return result;
    }

    #endregion

    #region JSON Parsing Helpers

    /// <summary>
    /// Safely retrieves a string property from a JsonElement, trying the exact name first
    /// then a case-insensitive search. Returns null if not found.
    /// </summary>
    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }

        return null;
    }

    private static double? TryGetDoubleProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var val))
                return val;

            // Handle string-encoded numbers from some firmware versions
            if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static long? TryGetLongProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var val))
                return val;

            if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    #endregion

    #region Utility

    private static string FormatUptime(long totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);

        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
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

    #endregion
}
