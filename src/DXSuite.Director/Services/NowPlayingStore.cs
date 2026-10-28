using System.Collections.Concurrent;
using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services;

public class NowPlayingState
{
    public required CurrentAssetInfo Asset { get; init; }
    public DateTime ReportedAtUtc { get; init; }
    public required string Source { get; init; } // "socket" | "heartbeat"
}

/// <summary>
/// Latest now-playing report per device, in memory only. Real-time reports arrive over the
/// wake socket at every asset transition; heartbeats refresh the same slot for players
/// without a socket. The Director live view subscribes to Changed for push updates.
/// </summary>
public class NowPlayingStore
{
    private readonly ConcurrentDictionary<(string TenantId, string DeviceId), NowPlayingState> _states = new();

    public event Action<string, string>? Changed;

    public void Report(string tenantId, string deviceId, CurrentAssetInfo asset, string source)
    {
        var state = new NowPlayingState
        {
            Asset = asset,
            ReportedAtUtc = DateTime.UtcNow,
            Source = source
        };

        // A stale heartbeat must not clobber a fresher socket report for the same asset;
        // heartbeats echo the last transition, which the socket already delivered
        if (source == "heartbeat"
            && _states.TryGetValue((tenantId, deviceId), out var existing)
            && existing.Source == "socket"
            && existing.Asset.Url == asset.Url
            && existing.Asset.StartedAtUtc >= asset.StartedAtUtc)
        {
            return;
        }

        _states[(tenantId, deviceId)] = state;
        Changed?.Invoke(tenantId, deviceId);
    }

    public NowPlayingState? TryGet(string tenantId, string deviceId) =>
        _states.TryGetValue((tenantId, deviceId), out var state) ? state : null;
}
