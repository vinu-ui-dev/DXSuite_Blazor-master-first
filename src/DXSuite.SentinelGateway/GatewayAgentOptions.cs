namespace DXSuite.SentinelGateway;

/// <summary>
/// Configuration for the edge gateway agent, bound from the "Gateway" section.
/// Each deployed gateway has its own TenantId + GatewayId identity and broker credentials.
/// </summary>
public class GatewayAgentOptions
{
    public const string SectionName = "Gateway";

    // Composable agent roles. The same installable agent can self-monitor its host,
    // run Tasks, and/or bridge other LAN devices, depending on which roles are enabled.
    public const string RoleGateway = "gateway";       // bridge/probe other LAN devices
    public const string RoleTasks = "tasks";           // run scripts on this host
    public const string RoleSelfMonitor = "selfmonitor"; // emit this host's own telemetry

    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public bool UseTls { get; set; }
    public string TenantId { get; set; } = "dev";
    /// <summary>Assigned identity. 0 (unset) means "not yet enrolled" — the agent will self-enroll if a token is present.</summary>
    public int GatewayId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    // ── Bulk enrollment (8E) ──
    /// <summary>One-time enrollment token; when set and GatewayId is 0, the agent claims its identity on first run.</summary>
    public string? EnrollmentToken { get; set; }
    /// <summary>Base URL of the cloud (tenant) the agent enrolls against, e.g. https://acme.dxsuite.app.</summary>
    public string? EnrollUrl { get; set; }

    public bool IsEnrolled => GatewayId > 0;
    public string Version { get; set; } = "1.0.0";
    public int HeartbeatSeconds { get; set; } = 30;
    public int ProbeTimeoutMs { get; set; } = 3000;

    /// <summary>
    /// Enabled roles. Left empty by default: config binding overlays a list by index rather than
    /// replacing it, so a non-empty default would leak stale entries when callers set fewer roles.
    /// EffectiveRoles supplies the legacy fallback (gateway + tasks) when this is empty.
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// Device id this agent publishes its own host telemetry under (selfmonitor role).
    /// Defaults to a deterministic id derived from the gateway identity so the cloud can map it
    /// to the host's DeviceInstance.
    /// </summary>
    public string? SelfDeviceId { get; set; }

    /// <summary>Loopback port for the local config/status UI.</summary>
    public int UiPort { get; set; } = 5099;

    public string ResolvedSelfDeviceId =>
        string.IsNullOrWhiteSpace(SelfDeviceId) ? $"host-gw{GatewayId}" : SelfDeviceId;

    /// <summary>Normalized, de-duplicated role set; defaults to gateway+tasks when none configured.</summary>
    public IReadOnlyCollection<string> EffectiveRoles =>
        (Roles is { Count: > 0 } ? Roles : new List<string> { RoleGateway, RoleTasks })
            .Select(r => r.Trim().ToLowerInvariant())
            .Where(r => r.Length > 0)
            .ToHashSet();

    public bool HasRole(string role) => EffectiveRoles.Contains(role.ToLowerInvariant());
}
