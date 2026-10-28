using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using DXSuite.Director.Hubs;

namespace DXSuite.Director.Services;

/// <summary>
/// Group naming shared by the wake hub and the notifier. The manifest group key uses the
/// same bucket id as the manifest cache (displayId for display-assigned devices, locationId
/// otherwise), so an invalidation ping reaches exactly the devices whose polls hit that bucket.
/// </summary>
public static class PlayerWakeGroups
{
    public static string Device(string tenantId, string deviceId) => $"wake:dev:{tenantId}:{deviceId}";
    public static string ManifestBucket(string tenantId, int bucketId) => $"wake:manifest:{tenantId}:{bucketId}";
}

/// <summary>
/// Tracks live wake-channel connections per device for the Devices UI badge and
/// dashboard count. Reference-counted: a device may briefly hold two connections
/// during a reconnect.
/// </summary>
public class PlayerWakeConnectionTracker
{
    private readonly ConcurrentDictionary<string, int> _connections = new(StringComparer.OrdinalIgnoreCase);

    public void OnConnected(string deviceId)
        => _connections.AddOrUpdate(deviceId, 1, (_, count) => count + 1);

    public void OnDisconnected(string deviceId)
    {
        while (_connections.TryGetValue(deviceId, out var count))
        {
            if (count <= 1)
            {
                if (_connections.TryRemove(new KeyValuePair<string, int>(deviceId, count)))
                    return;
            }
            else if (_connections.TryUpdate(deviceId, count - 1, count))
            {
                return;
            }
        }
    }

    public bool IsConnected(string deviceId) => _connections.ContainsKey(deviceId);

    public int ConnectedCount => _connections.Count;
}

/// <summary>
/// Fire-and-forget wake pings. The socket carries no content or commands; a ping only
/// tells players "poll now", so a failed or absent delivery degrades to the normal
/// poll cadence with zero correctness impact.
/// </summary>
public interface IPlayerWakeNotifier
{
    /// <summary>Wake a single device (queued command).</summary>
    void NotifyDevice(string tenantId, string deviceId);

    /// <summary>Wake every device polling a manifest bucket (content changed).</summary>
    void NotifyManifestBucket(string tenantId, int bucketId);
}

public class PlayerWakeNotifier : IPlayerWakeNotifier
{
    private readonly IHubContext<PlayerWakeHub> _hub;
    private readonly ILogger<PlayerWakeNotifier> _logger;

    public PlayerWakeNotifier(IHubContext<PlayerWakeHub> hub, ILogger<PlayerWakeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public void NotifyDevice(string tenantId, string deviceId)
        => Send(PlayerWakeGroups.Device(tenantId, deviceId), "command");

    public void NotifyManifestBucket(string tenantId, int bucketId)
        => Send(PlayerWakeGroups.ManifestBucket(tenantId, bucketId), "content");

    private void Send(string group, string reason)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.Clients.Group(group).SendAsync("wake", reason);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Wake ping to group {Group} failed", group);
            }
        });
    }
}
