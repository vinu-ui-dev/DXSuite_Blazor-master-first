using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Cross-source device identity resolution for multi-angle monitoring. Strong-key auto-link only
/// (decision 2026-06-22): a new source attaches to an existing canonical <see cref="DeviceInstance"/>
/// when its MAC matches exactly within the same customer; weaker signals are left to the manual merge UI.
/// Each source writes its own <see cref="DeviceSourceState"/> via <see cref="WriteSourceStateAsync"/>,
/// which recomputes the canonical rollup. (Serial-number matching is a planned follow-on — DeviceInstance
/// has no serial column yet; MAC is the available indexed strong key.)
/// </summary>
public static class DeviceIdentity
{
    public const string SourceCloudConnector = "cloud-connector";
    public const string SourceHeartbeat = "heartbeat";
    public const string SourceGateway = "gateway";
    public const string SourceAgentHost = "agent-host";

    public static string? NormalizeMac(string? mac) =>
        string.IsNullOrWhiteSpace(mac)
            ? null
            : new string(mac.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant() is { Length: > 0 } n ? n : null;

    /// <summary>Existing canonical device with the same normalized MAC under this customer, or null.</summary>
    public static async Task<DeviceInstance?> FindByStrongKeyAsync(ISentinelDBContext db, string customerId, string? mac, CancellationToken ct = default)
    {
        var norm = NormalizeMac(mac);
        if (norm is null) return null;

        // MACs are stored with assorted separators, so normalize in memory. Candidate sets per customer
        // are modest; the CustomerId + MacAddress indexes keep the prefilter cheap.
        var candidates = await db.DeviceInstances
            .Where(d => d.CustomerId == customerId && d.MacAddress != null)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(d => NormalizeMac(d.MacAddress) == norm);
    }

    /// <summary>
    /// Upsert one source's view of a device and recompute the canonical rollup onto DeviceInstance.
    /// Idempotent via the unique (DeviceInstanceId, SourceType, SourceKey) index.
    /// </summary>
    public static async Task WriteSourceStateAsync(
        ISentinelDBContext db, DeviceInstance device, string customerId,
        string sourceType, string sourceKey, DeviceState sourceState, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(sourceState);

        var existing = await db.DeviceSourceStates.FirstOrDefaultAsync(
            s => s.DeviceInstanceId == device.DeviceInstanceId && s.SourceType == sourceType && s.SourceKey == sourceKey, ct);

        if (existing is null)
        {
            db.DeviceSourceStates.Add(new DeviceSourceState
            {
                DeviceInstanceId = device.DeviceInstanceId,
                CustomerId = customerId,
                SourceType = sourceType,
                SourceKey = sourceKey,
                OverallHealth = (int)sourceState.OverallHealth,
                LastStateJson = json,
                LastSeenUtc = now,
                CreateDate = now
            });
        }
        else
        {
            existing.OverallHealth = (int)sourceState.OverallHealth;
            existing.LastStateJson = json;
            existing.LastSeenUtc = now;
            existing.LastEditDate = now;
        }
        await db.SaveChangesAsync(ct);

        // Recompute the canonical rollup across all of this device's sources.
        var sources = await db.DeviceSourceStates
            .Where(s => s.DeviceInstanceId == device.DeviceInstanceId)
            .ToListAsync(ct);
        device.LastStateJson = DeviceHealthRollup.BuildCanonicalStateJson(sources, DateTime.UtcNow);
        device.LastStateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
