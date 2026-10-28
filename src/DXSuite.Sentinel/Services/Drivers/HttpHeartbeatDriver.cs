using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Director.Services;
using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services.Drivers;

/// <summary>
/// Driver for devices that communicate via HTTP heartbeats (BrightSign, SignageOS, DirectorClient).
/// These devices push their state to Director — this driver reads the cached state from
/// SentinelDeviceInstance.LastStateJson rather than actively polling, since the heartbeat
/// mechanism already handles data collection.
/// </summary>
public class HttpHeartbeatDriver : IDeviceDriver
{
    public string ProtocolId => "http-heartbeat";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICecCommandService _cecCommandService;
    private readonly ILogger<HttpHeartbeatDriver> _logger;

    /// <summary>
    /// Devices with a heartbeat older than this threshold are considered offline
    /// </summary>
    private static readonly TimeSpan HeartbeatStaleThreshold = TimeSpan.FromMinutes(5);

    public HttpHeartbeatDriver(
        IServiceScopeFactory scopeFactory,
        ICecCommandService cecCommandService,
        ILogger<HttpHeartbeatDriver> logger)
    {
        _scopeFactory = scopeFactory;
        _cecCommandService = cecCommandService;
        _logger = logger;
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
            DataSource = new DataSourceInfo { HasHeartbeat = true, PrimarySource = "device-heartbeat" }
        };

        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var deviceInstance = await sentinelDb.DeviceInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceInstanceId == device.DeviceInstanceId, cancellationToken);

        if (deviceInstance == null)
        {
            _logger.LogWarning("HttpHeartbeatDriver: DeviceInstance {InstanceId} not found in Sentinel DB", device.DeviceInstanceId);
            state.OverallHealth = OverallHealthStatus.Unknown;
            return state;
        }

        // LastStateUpdated tracks when the heartbeat service last wrote state for this device
        var lastUpdate = deviceInstance.LastStateUpdated ?? DateTime.MinValue;
        state.LastHeartbeat = lastUpdate;
        state.DataSource.LastHeartbeatData = lastUpdate;

        var isStale = DateTime.UtcNow - lastUpdate > HeartbeatStaleThreshold;

        // Parse the cached device state from LastStateJson
        HeartbeatStateData? stateData = null;
        if (!string.IsNullOrEmpty(deviceInstance.LastStateJson))
        {
            try
            {
                stateData = JsonSerializer.Deserialize<HeartbeatStateData>(deviceInstance.LastStateJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize LastStateJson for device instance {InstanceId}", device.DeviceInstanceId);
            }
        }

        // Build monitor readings from descriptor-defined monitor points
        foreach (var mp in descriptor.MonitorPoints)
        {
            try
            {
                var reading = BuildMonitorReading(mp, deviceInstance, stateData, isStale);
                state.MonitorReadings[mp.Id] = reading;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error building monitor reading {MonitorPointId} for device {InstanceId}", mp.Id, device.DeviceInstanceId);
                state.MonitorReadings[mp.Id] = new MonitorReading
                {
                    MonitorPointId = mp.Id,
                    Name = mp.Name,
                    Status = "unknown",
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        state.OverallHealth = DetermineOverallHealth(state.MonitorReadings.Values, isStale);

        return state;
    }

    public Task<CommandResult> ExecuteCommandAsync(DeviceInstanceInfo device, string commandId, Dictionary<string, object>? parameters, DeviceDescriptorSchema descriptor, CancellationToken cancellationToken = default)
    {
        var result = new CommandResult { CommandId = commandId };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // DirectorDeviceId is used as the key for CEC command queuing
        var directorDeviceId = device.Config?.TryGetValue("directorDeviceId", out var devIdObj) == true
            ? devIdObj?.ToString()
            : null;

        if (string.IsNullOrEmpty(directorDeviceId))
        {
            result.ErrorMessage = "No directorDeviceId configured for this device instance";
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return Task.FromResult(result);
        }

        try
        {
            // Heartbeat-based devices receive commands via CEC piggyback on their next sync
            switch (commandId.ToLowerInvariant())
            {
                case "cec-power-on":
                    var powerOnCmd = new CecCommand
                    {
                        Action = "power_on",
                        Output = parameters?.TryGetValue("output", out var onOutput) == true ? onOutput?.ToString() : "HDMI-1"
                    };
                    _cecCommandService.QueueCommand(directorDeviceId, powerOnCmd);
                    result.Success = true;
                    result.Response = "CEC power-on command queued for next heartbeat sync";
                    result.RoutedVia = "heartbeat-piggyback";
                    break;

                case "cec-power-off":
                    var powerOffCmd = new CecCommand
                    {
                        Action = "power_off",
                        Output = parameters?.TryGetValue("output", out var offOutput) == true ? offOutput?.ToString() : "HDMI-1"
                    };
                    _cecCommandService.QueueCommand(directorDeviceId, powerOffCmd);
                    result.Success = true;
                    result.Response = "CEC power-off command queued for next heartbeat sync";
                    result.RoutedVia = "heartbeat-piggyback";
                    break;

                default:
                    var cmdDef = descriptor.Commands.FirstOrDefault(c => c.Id == commandId);
                    if (cmdDef?.Protocol?.Equals("cec", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var cecCmd = new CecCommand
                        {
                            Action = cmdDef.Command,
                            Output = parameters?.TryGetValue("output", out var output) == true ? output?.ToString() : null
                        };
                        _cecCommandService.QueueCommand(directorDeviceId, cecCmd);
                        result.Success = true;
                        result.Response = $"CEC command '{cmdDef.Command}' queued for next heartbeat sync";
                        result.RoutedVia = "heartbeat-piggyback";
                    }
                    else
                    {
                        result.ErrorMessage = $"Command '{commandId}' is not supported for heartbeat-based devices. " +
                                              "Only CEC commands can be piggybacked on heartbeat responses.";
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error executing command {CommandId} on heartbeat device {InstanceId}", commandId, device.DeviceInstanceId);
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
        }

        return Task.FromResult(result);
    }

    public async Task<bool> TestConnectionAsync(DeviceInstanceInfo device, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var deviceInstance = await sentinelDb.DeviceInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceInstanceId == device.DeviceInstanceId, cancellationToken);

        if (deviceInstance == null)
            return false;

        // A heartbeat device is "connected" if its state was updated within the staleness threshold
        return DateTime.UtcNow - (deviceInstance.LastStateUpdated ?? DateTime.MinValue) <= HeartbeatStaleThreshold;
    }

    /// <summary>
    /// Maps known monitor point IDs to data from the Sentinel device instance cached state
    /// </summary>
    private MonitorReading BuildMonitorReading(MonitorPointDefinition mp, DeviceInstance deviceInstance, HeartbeatStateData? stateData, bool isStale)
    {
        var lastUpdate = deviceInstance.LastStateUpdated ?? DateTime.MinValue;
        var reading = new MonitorReading
        {
            MonitorPointId = mp.Id,
            Name = mp.Name,
            Timestamp = lastUpdate
        };

        switch (mp.Id.ToLowerInvariant())
        {
            case "heartbeat-age":
                var age = DateTime.UtcNow - lastUpdate;
                reading.Value = (int)age.TotalSeconds;
                reading.DisplayValue = FormatTimeSpan(age);
                reading.Status = isStale ? "critical" : "healthy";
                break;

            case "storage-health":
                reading.Value = stateData?.StorageHealth;
                reading.DisplayValue = stateData?.StorageHealth.HasValue == true ? $"{stateData.StorageHealth}%" : "N/A";
                reading.Status = EvaluateThreshold(stateData?.StorageHealth, mp);
                break;

            case "storage-size":
                reading.Value = stateData?.StorageSize;
                reading.DisplayValue = stateData?.StorageSize.HasValue == true ? FormatBytes(stateData.StorageSize.Value) : "N/A";
                reading.Status = "healthy";
                break;

            case "content-health":
                var contentStatus = stateData?.ContentHealthStatus ?? "unknown";
                reading.Value = contentStatus;
                reading.DisplayValue = contentStatus;
                reading.Status = contentStatus switch
                {
                    "healthy" => "healthy",
                    "loading" => "healthy",
                    "unresponsive" => "warning",
                    "failed" => "critical",
                    _ => "unknown"
                };
                break;

            case "input-status":
                var inputStatus = stateData?.InputStatus ?? "unknown";
                reading.Value = inputStatus;
                reading.DisplayValue = inputStatus;
                // 'foreground' is the expected state; 'background' means wrong input source
                reading.Status = inputStatus switch
                {
                    "foreground" => "healthy",
                    "background" => "warning",
                    _ => "unknown"
                };
                break;

            case "connected-displays":
                var displaysJson = stateData?.ConnectedDisplays;
                reading.Value = displaysJson;
                reading.DisplayValue = !string.IsNullOrEmpty(displaysJson) ? "Connected" : "No data";
                reading.Status = !string.IsNullOrEmpty(displaysJson) ? "healthy" : "unknown";
                break;

            case "screenshot":
                var hasScreenshot = stateData?.ScreenshotCapturedAt != null;
                reading.Value = stateData?.ScreenshotCapturedAt;
                reading.DisplayValue = hasScreenshot
                    ? $"Captured {stateData!.ScreenshotCapturedAt:g}"
                    : "No screenshot";
                reading.Status = "healthy";
                break;

            case "client-version":
                reading.Value = stateData?.ClientAppVersion;
                reading.DisplayValue = stateData?.ClientAppVersion ?? "Unknown";
                reading.Status = !string.IsNullOrEmpty(stateData?.ClientAppVersion) ? "healthy" : "warning";
                break;

            case "ip-address":
                reading.Value = deviceInstance.IpAddress;
                reading.DisplayValue = deviceInstance.IpAddress ?? "Unknown";
                reading.Status = !string.IsNullOrEmpty(deviceInstance.IpAddress) ? "healthy" : "warning";
                break;

            default:
                reading.Status = "unknown";
                break;
        }

        // Override status if the device heartbeat is stale (device offline overrides point-level health)
        if (isStale && mp.Id.ToLowerInvariant() != "heartbeat-age")
        {
            reading.Status = reading.Status == "critical" ? "critical" : "warning";
        }

        return reading;
    }

    private static string EvaluateThreshold(int? value, MonitorPointDefinition mp)
    {
        if (!value.HasValue) return "unknown";

        if (mp.Thresholds?.Critical != null)
        {
            if (EvaluateNumericCondition(value.Value, mp.Thresholds.Critical))
                return "critical";
        }

        if (mp.Thresholds?.Warning != null)
        {
            if (EvaluateNumericCondition(value.Value, mp.Thresholds.Warning))
                return "warning";
        }

        return "healthy";
    }

    private static bool EvaluateNumericCondition(int value, ConditionExpression condition)
    {
        if (condition.Value == null) return false;

        if (!double.TryParse(condition.Value.ToString(), out var threshold))
            return false;

        return condition.Operator.ToLowerInvariant() switch
        {
            "<" => value < threshold,
            "<=" => value <= threshold,
            ">" => value > threshold,
            ">=" => value >= threshold,
            "==" => Math.Abs(value - threshold) < 0.001,
            "!=" => Math.Abs(value - threshold) >= 0.001,
            _ => false
        };
    }

    private static OverallHealthStatus DetermineOverallHealth(IEnumerable<MonitorReading> readings, bool isStale)
    {
        if (isStale) return OverallHealthStatus.Offline;

        var readingsList = readings.ToList();
        if (readingsList.Count == 0) return OverallHealthStatus.Unknown;
        if (readingsList.Any(r => r.Status == "critical")) return OverallHealthStatus.Critical;
        if (readingsList.Any(r => r.Status == "warning")) return OverallHealthStatus.Warning;
        if (readingsList.All(r => r.Status == "healthy")) return OverallHealthStatus.Healthy;
        return OverallHealthStatus.Unknown;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h ago";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m ago";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m ago";
        return $"{(int)ts.TotalSeconds}s ago";
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {suffixes[order]}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Structured representation of heartbeat state data stored in DeviceInstance.LastStateJson.
/// Written by the HeartbeatService when processing incoming device heartbeats.
/// </summary>
public class HeartbeatStateData
{
    public int? StorageHealth { get; set; }
    public long? StorageSize { get; set; }
    public string? StorageType { get; set; }
    public string? ContentHealthStatus { get; set; }
    public string? InputStatus { get; set; }
    public string? ConnectedDisplays { get; set; }
    public DateTime? ScreenshotCapturedAt { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? ClientAppVersion { get; set; }
    public string? IpAddress { get; set; }
    public string? OsVersion { get; set; }
}
