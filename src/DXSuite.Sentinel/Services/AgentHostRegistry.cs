using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Contracts;
using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Represents a self-monitoring agent's host as a first-class <see cref="DeviceInstance"/>.
/// On telemetry it upserts the host device (under the auto-created "sentinel-agent-host" descriptor +
/// version, linked to the agent's own gateway so Tasks resolve to it) and evaluates the host metrics
/// against the descriptor thresholds so the device carries real Healthy/Warning/Critical state for the
/// evaluation pipeline. On gateway offline it flips the host device to Offline. Shared by the ingestion
/// subscriber and the gateway connection manager.
/// </summary>
public class AgentHostRegistry
{
    public const string DescriptorType = "sentinel-agent-host";

    // Health monitor points for an agent host. Single source of truth for both the published descriptor
    // version and the live status evaluation, so the two never drift.
    private static readonly MonitorPointDefinition[] MonitorPoints =
    {
        new()
        {
            Id = "cpu-percent", Name = "CPU", Category = "health", DataType = "float", Unit = "%",
            Thresholds = new ThresholdConfig
            {
                Warning = new ConditionExpression { Operator = ">", Value = 85.0 },
                Critical = new ConditionExpression { Operator = ">", Value = 95.0 }
            },
            Description = "Overall CPU utilization"
        },
        new()
        {
            Id = "memory-used-percent", Name = "Memory used", Category = "health", DataType = "float", Unit = "%",
            Thresholds = new ThresholdConfig
            {
                Warning = new ConditionExpression { Operator = ">", Value = 85.0 },
                Critical = new ConditionExpression { Operator = ">", Value = 95.0 }
            },
            Description = "Physical memory in use"
        },
        new()
        {
            Id = "disk-used-percent", Name = "Disk used", Category = "health", DataType = "float", Unit = "%",
            Thresholds = new ThresholdConfig
            {
                Warning = new ConditionExpression { Operator = ">", Value = 85.0 },
                Critical = new ConditionExpression { Operator = ">", Value = 95.0 }
            },
            Description = "Highest fixed-drive utilization"
        },
        new()
        {
            Id = "uptime-seconds", Name = "Uptime", Category = "status", DataType = "integer", Unit = "s",
            Description = "Seconds since last boot"
        }
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentHostRegistry> _logger;

    public AgentHostRegistry(IServiceScopeFactory scopeFactory, ILogger<AgentHostRegistry> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Create or update the host DeviceInstance from a self-monitoring agent's state message.</summary>
    public async Task UpsertFromStateAsync(DeviceStateMessage msg)
    {
        if (msg.GatewayId is not int gatewayId)
            return; // not an agent-host telemetry message

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            var (descriptorId, versionId) = await EnsureDescriptorAsync(db);

            // One host per gateway: key the instance by (gateway, agent-host descriptor).
            var host = await db.DeviceInstances
                .FirstOrDefaultAsync(d => d.SiteGatewayId == gatewayId && d.DescriptorId == descriptorId);

            var metrics = ParseMetrics(msg.StateJson);
            var displayName = string.IsNullOrWhiteSpace(metrics?.MachineName)
                ? $"Agent host (gateway {gatewayId})" : metrics!.MachineName;
            var state = BuildState(metrics, msg.Online);

            if (host is null)
            {
                // Only create once the gateway actually exists, to satisfy the SiteGateway FK.
                var gatewayExists = await db.SiteGateways.AnyAsync(g => g.GatewayId == gatewayId);
                if (!gatewayExists)
                {
                    _logger.LogDebug("Agent-host telemetry for unknown gateway {GatewayId}; skipping registration", gatewayId);
                    return;
                }

                host = new DeviceInstance
                {
                    DescriptorId = descriptorId,
                    DescriptorVersionId = versionId,
                    SiteGatewayId = gatewayId,
                    CustomerId = msg.TenantId,
                    DisplayName = displayName,
                    IsEnabled = true,
                    LastStateUpdated = DateTime.UtcNow,
                    CreateDate = DateTime.UtcNow,
                    CommissionedSource = "auto"
                };
                db.DeviceInstances.Add(host);
                await db.SaveChangesAsync(); // assign DeviceInstanceId before writing its source state
                _logger.LogInformation("Registered agent host '{Name}' as device for gateway {GatewayId}", displayName, gatewayId);
            }
            else
            {
                host.DisplayName = displayName;
                host.DescriptorVersionId = versionId; // ensure evaluation is active (and picks up threshold updates)
                host.LastEditDate = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            // Record this agent's own view as a source angle; the rollup recomputes the canonical state.
            await DeviceIdentity.WriteSourceStateAsync(
                db, host, host.CustomerId, DeviceIdentity.SourceAgentHost, gatewayId.ToString(), state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert agent host for gateway {GatewayId}", gatewayId);
        }
    }

    /// <summary>Flip all host devices served by a gateway to Offline (called when the gateway drops).</summary>
    public async Task MarkOfflineByGatewayAsync(int gatewayId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            var descriptorId = await db.DeviceDescriptors
                .Where(d => d.DeviceType == DescriptorType)
                .Select(d => (int?)d.DescriptorId)
                .FirstOrDefaultAsync();
            if (descriptorId is null) return;

            var hosts = await db.DeviceInstances
                .Where(d => d.SiteGatewayId == gatewayId && d.DescriptorId == descriptorId.Value)
                .ToListAsync();
            if (hosts.Count == 0) return;

            foreach (var host in hosts)
            {
                // Flip the agent-host source to Offline; the rollup folds it into the canonical state.
                await DeviceIdentity.WriteSourceStateAsync(
                    db, host, host.CustomerId, DeviceIdentity.SourceAgentHost,
                    (host.SiteGatewayId ?? gatewayId).ToString(), BuildState(null, online: false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Marking agent host offline for gateway {GatewayId} failed: {Message}", gatewayId, ex.Message);
        }
    }

    private static async Task<(int descriptorId, int versionId)> EnsureDescriptorAsync(ISentinelDBContext db)
    {
        var descriptor = await db.DeviceDescriptors
            .FirstOrDefaultAsync(d => d.DeviceType == DescriptorType && d.IsActive);

        if (descriptor is null)
        {
            descriptor = new DeviceDescriptor
            {
                DeviceType = DescriptorType,
                DisplayName = "Sentinel Agent Host",
                Category = "sentinel-agent",
                IsActive = true,
                CreateDate = DateTime.UtcNow
            };
            db.DeviceDescriptors.Add(descriptor);
            await db.SaveChangesAsync();
        }

        var version = await db.DeviceDescriptorVersions
            .FirstOrDefaultAsync(v => v.DescriptorId == descriptor.DescriptorId && v.IsActive);

        if (version is null)
        {
            version = new DeviceDescriptorVersion
            {
                DescriptorId = descriptor.DescriptorId,
                Version = "1.0",
                DescriptorJson = JsonSerializer.Serialize(BuildDescriptorSchema()),
                IsActive = true,
                PublishedAt = DateTime.UtcNow,
                CreateDate = DateTime.UtcNow,
                CreatedBy = "system"
            };
            db.DeviceDescriptorVersions.Add(version);
            await db.SaveChangesAsync();
        }

        return (descriptor.DescriptorId, version.VersionId);
    }

    private static DeviceDescriptorSchema BuildDescriptorSchema() => new()
    {
        DeviceType = DescriptorType,
        DisplayName = "Sentinel Agent Host",
        Category = "sentinel-agent",
        Communication = new CommunicationConfig
        {
            Protocol = "sentinel-agent",
            ConnectionType = "agent-telemetry",
            HeartbeatMethod = new HeartbeatMethodConfig { Type = "push", IntervalSeconds = 30 }
        },
        MonitorPoints = MonitorPoints.ToList()
    };

    private static HostMetrics? ParseMetrics(string? stateJson)
    {
        if (string.IsNullOrEmpty(stateJson)) return null;
        try
        {
            var metrics = SentinelJson.Deserialize<HostMetrics>(stateJson);
            return string.IsNullOrEmpty(metrics?.MachineName) ? null : metrics;
        }
        catch { return null; }
    }

    private static DeviceState BuildState(HostMetrics? m, bool online)
    {
        var state = new DeviceState
        {
            DeviceType = DescriptorType,
            DisplayName = m?.MachineName ?? "Agent host",
            StateTimestamp = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow,
            DataSource = new DataSourceInfo { PrimarySource = "sentinel-agent", HasHeartbeat = true, LastHeartbeatData = DateTime.UtcNow },
            MonitorReadings = new Dictionary<string, MonitorReading>(),
            ActiveAlerts = new List<ActiveAlert>()
        };

        if (m is not null)
        {
            AddReading(state, "cpu-percent", "CPU", m.CpuPercent, "%");
            AddReading(state, "memory-used-percent", "Memory used", m.MemoryUsedPercent, "%");
            var topDisk = m.Disks.OrderByDescending(d => d.UsedPercent).FirstOrDefault();
            if (topDisk is not null)
                AddReading(state, "disk-used-percent", $"Disk {topDisk.Name}", topDisk.UsedPercent, "%");
            AddReading(state, "uptime-seconds", "Uptime", m.UptimeSeconds, "s");
        }

        state.OverallHealth = !online
            ? OverallHealthStatus.Offline
            : DetermineOverallHealth(state.MonitorReadings.Values);

        return state;
    }

    private static void AddReading(DeviceState state, string id, string name, object? value, string unit)
    {
        if (value is null) return;

        var status = EvaluateStatus(id, value);
        var reading = new MonitorReading
        {
            MonitorPointId = id,
            Name = name,
            Value = value,
            DisplayValue = $"{value}{unit}",
            Status = status,
            Timestamp = DateTime.UtcNow
        };
        state.MonitorReadings[id] = reading;

        if (status is "warning" or "critical")
        {
            state.ActiveAlerts.Add(new ActiveAlert
            {
                AlertId = id,
                MonitorPointId = id,
                Severity = status,
                Message = $"{name} is {reading.DisplayValue} ({status} threshold breached)",
                TriggeredAt = DateTime.UtcNow
            });
        }
    }

    private static string EvaluateStatus(string monitorPointId, object value)
    {
        var mp = MonitorPoints.FirstOrDefault(p => p.Id == monitorPointId);
        if (mp?.Thresholds is null) return "healthy";
        if (!double.TryParse(value.ToString(), out var v)) return "unknown";
        if (mp.Thresholds.Critical is not null && EvaluateNumericCondition(v, mp.Thresholds.Critical)) return "critical";
        if (mp.Thresholds.Warning is not null && EvaluateNumericCondition(v, mp.Thresholds.Warning)) return "warning";
        return "healthy";
    }

    private static bool EvaluateNumericCondition(double value, ConditionExpression condition)
    {
        if (condition.Value is null || !double.TryParse(condition.Value.ToString(), out var threshold)) return false;
        return condition.Operator switch
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

    private static OverallHealthStatus DetermineOverallHealth(IEnumerable<MonitorReading> readings)
    {
        var list = readings.ToList();
        if (list.Count == 0) return OverallHealthStatus.Unknown;
        if (list.Any(r => r.Status == "critical")) return OverallHealthStatus.Critical;
        if (list.Any(r => r.Status == "warning")) return OverallHealthStatus.Warning;
        return OverallHealthStatus.Healthy;
    }
}
