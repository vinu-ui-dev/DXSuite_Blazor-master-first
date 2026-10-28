using System.Text.Json;
using System.Text.Json.Serialization;

namespace DXSuite.Sentinel.Contracts;

/// <summary>
/// Shared JSON serialization options for all Sentinel wire messages — camelCase,
/// case-insensitive on read, enums as strings. One contract for the cloud, the
/// gateway agent, and the SignageOS DirectorClient (JavaScript) to agree on.
/// </summary>
public static class SentinelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Agent → cloud enrollment request (HTTP POST /api/sentinel/gateway/enroll).</summary>
public class GatewayEnrollRequest
{
    public string Token { get; set; } = string.Empty;
    /// <summary>Stable per-machine id (Windows MachineGuid / Linux machine-id + hostname); idempotency key.</summary>
    public string MachineFingerprint { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    /// <summary>Optional role override; falls back to the token's default roles.</summary>
    public string? Roles { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>Cloud → agent enrollment result. The ApiKey is returned once and not recoverable later.</summary>
public class GatewayEnrollResponse
{
    public bool Success { get; set; }
    public int GatewayId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? Roles { get; set; }
    public string? BrokerHost { get; set; }
    public int BrokerPort { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Device telemetry snapshot published to .../dev/{id}/state (retained).</summary>
public class DeviceStateMessage
{
    public string TenantId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string TimestampUtc { get; set; } = string.Empty;
    public bool Online { get; set; }
    /// <summary>Owning gateway id when this device is a self-monitoring agent host; null for other devices.</summary>
    public int? GatewayId { get; set; }
    /// <summary>Serialized DeviceState (the existing cloud-side telemetry payload), or null.</summary>
    public string? StateJson { get; set; }
}

/// <summary>
/// Periodic host health published by a self-monitoring agent to .../dev/{id}/health.
/// Carries the agent host's own resource metrics (the "self-monitor" role).
/// </summary>
public class DeviceHealthMessage
{
    public string TenantId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string TimestampUtc { get; set; } = string.Empty;
    public int? GatewayId { get; set; }
    public HostMetrics? Metrics { get; set; }
}

/// <summary>A self-monitoring agent host's resource snapshot. Null fields mean "not available on this platform".</summary>
public class HostMetrics
{
    public string MachineName { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string OsArchitecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public long? UptimeSeconds { get; set; }
    public double? CpuPercent { get; set; }
    public long? MemoryTotalBytes { get; set; }
    public long? MemoryAvailableBytes { get; set; }
    public double? MemoryUsedPercent { get; set; }
    public List<DiskInfo> Disks { get; set; } = new();
    /// <summary>Watched service states (Windows). Empty until the service-watch feature is wired.</summary>
    public List<ServiceStatus> Services { get; set; } = new();
}

public class DiskInfo
{
    public string Name { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public double UsedPercent { get; set; }
}

public class ServiceStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>Gateway heartbeat published to .../gw/{id}/state (retained).</summary>
public class GatewayHeartbeatMessage
{
    public string TenantId { get; set; } = string.Empty;
    public string GatewayId { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string TimestampUtc { get; set; } = string.Empty;
}

/// <summary>
/// Last Will &amp; Testament payload registered by a device or gateway; the broker
/// publishes it to the .../lwt topic when the client disconnects ungracefully.
/// </summary>
public class LastWillMessage
{
    public string TenantId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty; // "dev" or "gw"
    public string EntityId { get; set; } = string.Empty;
    public string Reason { get; set; } = "connection-lost";
}

/// <summary>
/// Cloud → gateway probe/command, published to .../gw/{id}/cmd. The agent runs it
/// against a LAN device and replies on the response topic carried in the MQTT 5
/// response-topic property (correlation-data carries the request id).
/// </summary>
public class GatewayCommandMessage
{
    /// <summary>Correlation id echoed back in the response so the cloud can match the reply.</summary>
    public string RequestId { get; set; } = string.Empty;
    public string CommandId { get; set; } = string.Empty;
    public string DeviceIp { get; set; } = string.Empty;
    public int? DevicePort { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    /// <summary>Device descriptor JSON so the agent knows how to talk to the device.</summary>
    public string? DescriptorJson { get; set; }

    // ── Task / script execution (CommandId = "run-script") ──
    /// <summary>Interpreter for the script: "powershell", "cmd", or "bash".</summary>
    public string? ScriptType { get; set; }
    /// <summary>The script body to run on the agent host.</summary>
    public string? ScriptContent { get; set; }
    /// <summary>Lowercase hex SHA-256 of <see cref="ScriptContent"/>; the agent runs only if it matches.</summary>
    public string? ScriptHash { get; set; }
    /// <summary>Hard timeout for the script; the agent kills the process if exceeded.</summary>
    public int? TimeoutMs { get; set; }
}

/// <summary>Gateway → cloud reply to a <see cref="GatewayCommandMessage"/>.</summary>
public class GatewayResponseMessage
{
    /// <summary>Echoes <see cref="GatewayCommandMessage.RequestId"/> for correlation.</summary>
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ResponseData { get; set; }

    // ── Task / script execution results ──
    public int? ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
}
