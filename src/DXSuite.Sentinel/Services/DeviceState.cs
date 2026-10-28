namespace DXSuite.Sentinel.Services;

/// <summary>
/// Runtime representation of a device's current state, aggregated from all data sources.
/// This is an in-memory model used by the Sentinel engine, not a database entity.
/// </summary>
public class DeviceState
{
    public int DeviceInstanceId { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public OverallHealthStatus OverallHealth { get; set; } = OverallHealthStatus.Unknown;
    public DateTime? LastHeartbeat { get; set; }
    public DateTime StateTimestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, MonitorReading> MonitorReadings { get; set; } = new();
    public List<ActiveAlert> ActiveAlerts { get; set; } = new();
    public DataSourceInfo DataSource { get; set; } = new();

    public MonitorReading? GetMonitorPoint(string monitorPointId)
    {
        return MonitorReadings.TryGetValue(monitorPointId, out var reading) ? reading : null;
    }
}

public enum OverallHealthStatus
{
    Healthy,
    Warning,
    Critical,
    Offline,
    Unknown
}

public class MonitorReading
{
    public string MonitorPointId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string? DisplayValue { get; set; }
    public string Status { get; set; } = "unknown";
    public DateTime Timestamp { get; set; }
}

public class ActiveAlert
{
    public string AlertId { get; set; } = string.Empty;
    public string MonitorPointId { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

public class DataSourceInfo
{
    public bool HasHeartbeat { get; set; }
    public bool HasGateway { get; set; }
    public bool HasCloudPlatform { get; set; }
    public string? PrimarySource { get; set; }
    public DateTime? LastHeartbeatData { get; set; }
    public DateTime? LastGatewayData { get; set; }
    public DateTime? LastPlatformData { get; set; }
}
