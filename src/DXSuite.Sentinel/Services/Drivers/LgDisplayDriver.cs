using Microsoft.Extensions.Logging;
using DXSuite.Director.Services;
using DXSuite.Sentinel.Services;

namespace DXSuite.Sentinel.Services.Drivers;

/// <summary>
/// Device driver for LG commercial displays via TCP RS-232 over IP (port 9761).
/// Wraps the existing LgDisplayControlService to work within the Sentinel descriptor-driven model.
/// </summary>
public class LgDisplayDriver : IDeviceDriver
{
    public string ProtocolId => "tcp-rs232";

    private readonly ILgDisplayControlService _lgControl;
    private readonly ILogger<LgDisplayDriver> _logger;

    public LgDisplayDriver(ILgDisplayControlService lgControl, ILogger<LgDisplayDriver> logger)
    {
        _lgControl = lgControl;
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
            DataSource = new DataSourceInfo { HasGateway = true, PrimarySource = "gateway-lan" }
        };

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            state.OverallHealth = OverallHealthStatus.Unknown;
            return state;
        }

        var setId = device.Config?.TryGetValue("setId", out var setIdObj) == true && setIdObj is int sid ? sid : 1;

        // Poll each monitor point defined in the descriptor
        foreach (var mp in descriptor.MonitorPoints)
        {
            try
            {
                var reading = await PollMonitorPointAsync(device.IpAddress, setId, mp, cancellationToken);
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
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            result.ErrorMessage = "Device IP address not configured";
            return result;
        }

        var setId = device.Config?.TryGetValue("setId", out var setIdObj) == true && setIdObj is int sid ? sid : 1;

        try
        {
            LgCommandResult lgResult;
            switch (commandId.ToLowerInvariant())
            {
                case "power-on":
                    lgResult = await _lgControl.PowerOnAsync(device.IpAddress, setId);
                    break;
                case "power-off":
                    lgResult = await _lgControl.PowerOffAsync(device.IpAddress, setId);
                    break;
                case "power-cycle":
                    var delayMs = parameters?.TryGetValue("delayMs", out var delay) == true ? Convert.ToInt32(delay) : 5000;
                    lgResult = await _lgControl.PowerCycleAsync(device.IpAddress, setId, delayMs, macAddress: device.MacAddress);
                    break;
                case "get-power-status":
                    lgResult = await _lgControl.GetPowerStatusAsync(device.IpAddress, setId);
                    break;
                default:
                    // For descriptor-defined commands, send the raw command string from the definition
                    var cmdDef = descriptor.Commands.FirstOrDefault(c => c.Id == commandId);
                    if (cmdDef != null)
                    {
                        lgResult = await _lgControl.SendCommandAsync(device.IpAddress, cmdDef.Command);
                    }
                    else
                    {
                        result.ErrorMessage = $"Unknown command: {commandId}";
                        return result;
                    }
                    break;
            }

            result.Success = lgResult.Success;
            result.Response = lgResult.Response;
            result.ErrorMessage = lgResult.ErrorMessage;
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
        return await _lgControl.TestConnectionAsync(device.IpAddress);
    }

    private async Task<MonitorReading> PollMonitorPointAsync(string ipAddress, int setId, MonitorPointDefinition mp, CancellationToken ct)
    {
        var reading = new MonitorReading
        {
            MonitorPointId = mp.Id,
            Name = mp.Name,
            Timestamp = DateTime.UtcNow
        };

        switch (mp.Id.ToLowerInvariant())
        {
            case "power-state":
                var powerResult = await _lgControl.GetPowerStatusAsync(ipAddress, setId);
                if (powerResult.Success)
                {
                    var isOn = powerResult.PowerState == 1;
                    reading.Value = isOn ? "on" : "off";
                    reading.DisplayValue = isOn ? "On" : "Off";
                    reading.Status = EvaluateCondition(reading.Value, mp) ? "healthy" : "critical";
                }
                else
                {
                    reading.Value = "unreachable";
                    reading.Status = "critical";
                    reading.DisplayValue = "Unreachable";
                }
                break;

            default:
                // For other monitor points, try sending the retrieve command from the descriptor
                if (!string.IsNullOrEmpty(mp.RetrieveCommand))
                {
                    var cmdResult = await _lgControl.SendCommandAsync(ipAddress, mp.RetrieveCommand);
                    reading.Value = cmdResult.Response;
                    reading.DisplayValue = cmdResult.Response;
                    reading.Status = cmdResult.Success ? "healthy" : "warning";
                }
                else
                {
                    reading.Status = "unknown";
                }
                break;
        }

        return reading;
    }

    private static bool EvaluateCondition(object? value, MonitorPointDefinition mp)
    {
        if (mp.ExpectedState == null) return true;
        var strValue = value?.ToString() ?? "";
        return mp.ExpectedState.Operator.ToLowerInvariant() switch
        {
            "==" => strValue.Equals(mp.ExpectedState.Value?.ToString(), StringComparison.OrdinalIgnoreCase),
            "!=" => !strValue.Equals(mp.ExpectedState.Value?.ToString(), StringComparison.OrdinalIgnoreCase),
            "notempty" => !string.IsNullOrEmpty(strValue),
            _ => true
        };
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
