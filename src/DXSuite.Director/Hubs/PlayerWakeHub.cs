using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Hubs;

/// <summary>
/// Push-to-poll wake channel. Carries no content and no commands: a "wake" message tells
/// the player to run its normal sync immediately, so a dead socket simply degrades to the
/// regular poll cadence. Connections authenticate with the device token (access_token
/// query param) and join a per-device group plus the manifest-bucket group matching the
/// cache bucket their polls hit.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
public class PlayerWakeHub : Hub
{
    private readonly IDirectorDBContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly PlayerWakeConnectionTracker _tracker;
    private readonly NowPlayingStore _nowPlaying;
    private readonly ILogger<PlayerWakeHub> _logger;

    public PlayerWakeHub(
        IDirectorDBContext db,
        ITenantContext tenantContext,
        PlayerWakeConnectionTracker tracker,
        NowPlayingStore nowPlaying,
        ILogger<PlayerWakeHub> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tracker = tracker;
        _nowPlaying = nowPlaying;
        _logger = logger;
    }

    /// <summary>
    /// Real-time now-playing report, invoked by the applet on each asset transition.
    /// Upstream reporting only; the server-to-player direction still carries no content.
    /// </summary>
    public Task ReportNowPlaying(Models.BrightSign.CurrentAssetInfo payload)
    {
        if (payload != null
            && Context.Items.TryGetValue("deviceId", out var value) && value is string deviceId)
        {
            _nowPlaying.Report(_tenantContext.TenantId, deviceId, payload, "socket");
        }
        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        var deviceId = ResolveDeviceId();
        if (string.IsNullOrEmpty(deviceId))
        {
            _logger.LogWarning("Wake channel connection without a device id; disconnecting");
            Context.Abort();
            return;
        }

        Context.Items["deviceId"] = deviceId;
        await Groups.AddToGroupAsync(Context.ConnectionId, PlayerWakeGroups.Device(_tenantContext.TenantId, deviceId));

        // Join the manifest bucket this device's polls read: displayId for display-assigned
        // devices, locationId otherwise (mirrors the manifest cache key space)
        var device = await _db.TblDevices
            .AsNoTracking()
            .Where(d => d.DeviceId == deviceId)
            .Select(d => new { d.DisplayId, d.LocationId })
            .FirstOrDefaultAsync();

        var bucketId = device?.DisplayId ?? device?.LocationId;
        if (bucketId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PlayerWakeGroups.ManifestBucket(_tenantContext.TenantId, bucketId.Value));
        }

        _tracker.OnConnected(deviceId);
        _logger.LogDebug("Wake channel connected: device {DeviceId}, bucket {BucketId}", deviceId, bucketId);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("deviceId", out var value) && value is string deviceId)
        {
            _tracker.OnDisconnected(deviceId);
        }
        return base.OnDisconnectedAsync(exception);
    }

    private string? ResolveDeviceId()
    {
        // Prefer the token-bound device id; fall back to the client-supplied query param
        // for tokens not yet bound to a device (first connection after pairing). A spoofed
        // id only subscribes to another device's wake pings, which carry no data.
        var fromToken = Context.GetHttpContext()?.Items["DeviceId"] as string;
        if (!string.IsNullOrEmpty(fromToken))
            return fromToken;

        return Context.GetHttpContext()?.Request.Query["deviceId"].ToString();
    }
}
