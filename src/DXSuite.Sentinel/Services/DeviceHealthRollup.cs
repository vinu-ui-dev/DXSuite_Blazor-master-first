using System.Text.Json;
using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Combines a device's per-source <see cref="DeviceSourceState"/> rows into the canonical health +
/// merged <see cref="DeviceState"/> stored on DeviceInstance.LastStateJson. Policy is worst-of across
/// non-stale sources (decision 2026-06-22): the most severe non-stale source wins, a fresh source that
/// reports Offline while others are healthy surfaces as Warning (a conflict, not silently healthy), and
/// the device is only Offline when every source is stale/offline.
/// </summary>
public static class DeviceHealthRollup
{
    /// <summary>A source older than this is treated as stale and excluded from the worst-of vote.</summary>
    public static readonly TimeSpan StalenessWindow = TimeSpan.FromMinutes(10);

    public static OverallHealthStatus Roll(IReadOnlyCollection<DeviceSourceState> sources, DateTime nowUtc)
    {
        if (sources.Count == 0) return OverallHealthStatus.Unknown;

        var fresh = sources.Where(s => nowUtc - s.LastSeenUtc <= StalenessWindow).ToList();
        if (fresh.Count == 0) return OverallHealthStatus.Offline; // all stale

        var statuses = fresh.Select(s => (OverallHealthStatus)s.OverallHealth).ToList();
        if (statuses.Any(s => s == OverallHealthStatus.Critical)) return OverallHealthStatus.Critical;
        if (statuses.Any(s => s == OverallHealthStatus.Warning)) return OverallHealthStatus.Warning;
        // A fresh source explicitly Offline while others are fine is a conflict — surface it, don't bury it.
        if (statuses.Any(s => s == OverallHealthStatus.Offline) && statuses.Any(s => s == OverallHealthStatus.Healthy))
            return OverallHealthStatus.Warning;
        if (statuses.All(s => s == OverallHealthStatus.Offline)) return OverallHealthStatus.Offline;
        if (statuses.Any(s => s == OverallHealthStatus.Healthy)) return OverallHealthStatus.Healthy;
        return OverallHealthStatus.Unknown;
    }

    /// <summary>
    /// Build the canonical merged DeviceState: rolled-up OverallHealth, union of all source
    /// MonitorReadings (newest source wins on a key collision so the bare monitor-point ids the evaluator
    /// keys on stay intact), aggregated alerts, and DataSourceInfo flags set per present source type.
    /// </summary>
    public static string BuildCanonicalStateJson(IReadOnlyCollection<DeviceSourceState> sources, DateTime nowUtc) =>
        JsonSerializer.Serialize(BuildState(sources, nowUtc));

    /// <summary>
    /// The canonical state as an object. Callers that need current health, such as the Pulse publisher,
    /// re-roll from the source rows with this rather than reading LastStateJson, which is recomputed only
    /// when a source reports and so never decays to Offline.
    /// </summary>
    public static DeviceState BuildState(IReadOnlyCollection<DeviceSourceState> sources, DateTime nowUtc)
    {
        var canonical = new DeviceState
        {
            OverallHealth = Roll(sources, nowUtc),
            StateTimestamp = nowUtc,
            LastHeartbeat = nowUtc,
            MonitorReadings = new Dictionary<string, MonitorReading>(),
            ActiveAlerts = new List<ActiveAlert>(),
            DataSource = new DataSourceInfo()
        };

        // Oldest first so the newest source overwrites on collisions.
        foreach (var src in sources.OrderBy(s => s.LastSeenUtc))
        {
            DeviceState? ss = null;
            try { ss = string.IsNullOrEmpty(src.LastStateJson) ? null : JsonSerializer.Deserialize<DeviceState>(src.LastStateJson); }
            catch { /* skip an unparseable source */ }
            if (ss is null) continue;

            if (!string.IsNullOrEmpty(ss.DisplayName)) canonical.DisplayName = ss.DisplayName;
            if (!string.IsNullOrEmpty(ss.DeviceType)) canonical.DeviceType = ss.DeviceType;
            if (!string.IsNullOrEmpty(ss.IpAddress)) canonical.IpAddress = ss.IpAddress;

            foreach (var kv in ss.MonitorReadings) canonical.MonitorReadings[kv.Key] = kv.Value;
            if (ss.ActiveAlerts is { Count: > 0 }) canonical.ActiveAlerts.AddRange(ss.ActiveAlerts);

            switch (src.SourceType)
            {
                case "heartbeat": canonical.DataSource.HasHeartbeat = true; canonical.DataSource.LastHeartbeatData = src.LastSeenUtc; break;
                case "gateway": canonical.DataSource.HasGateway = true; canonical.DataSource.LastGatewayData = src.LastSeenUtc; break;
                case "agent-host": canonical.DataSource.HasGateway = true; canonical.DataSource.LastGatewayData = src.LastSeenUtc; break;
                case "cloud-connector": canonical.DataSource.HasCloudPlatform = true; canonical.DataSource.LastPlatformData = src.LastSeenUtc; break;
            }
            canonical.DataSource.PrimarySource ??= src.SourceType;
        }

        return canonical;
    }
}
