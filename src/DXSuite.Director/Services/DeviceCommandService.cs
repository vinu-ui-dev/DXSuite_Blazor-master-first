using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public static class DeviceCommandTypes
{
    public const string Cec = "cec";
    public const string Screenshot = "screenshot";
    public const string FlushCache = "flushCache";
}

public static class DeviceCommandStatus
{
    public const string Queued = "Queued";
    public const string Delivered = "Delivered";
    public const string Acknowledged = "Acknowledged";
    public const string Expired = "Expired";
    public const string Superseded = "Superseded";
}

public class CecCommandPayload
{
    public string Action { get; set; } = string.Empty;
    public string? Output { get; set; }
}

/// <summary>
/// SQL-backed device command queue (tblDeviceCommand). Scoped: uses the request's
/// tenant-bound DbContext, so commands land in the DB of the tenant issuing them.
/// Singleton callers bridge through <see cref="CecCommandService"/>.
/// </summary>
public interface IDeviceCommandService
{
    Task QueueCecAsync(string deviceId, string action, string? output, string? issuedBy);

    /// <summary>Queues a screenshot capture; returns the request id the device will upload against.</summary>
    Task<string> QueueScreenshotAsync(string deviceId, string? issuedBy);

    Task QueueFlushCacheAsync(string deviceId, string? issuedBy);

    /// <summary>
    /// Drains the device's queue on its poll: rows past their expiry are marked Expired,
    /// the rest are marked Delivered and returned.
    /// </summary>
    Task<List<TblDeviceCommand>> DequeuePendingAsync(string deviceId);

    /// <summary>
    /// Resolve a screenshot request id a device is uploading against, returning its device id.
    /// Peeks rather than consumes: one capture may POST several times (per-platform formats,
    /// transient retries); the short window and latest-only storage bound any replay.
    /// </summary>
    Task<string?> ResolveScreenshotUploadAsync(string requestId);

    /// <summary>Marks a delivered screenshot command Acknowledged once its upload is stored.</summary>
    Task AcknowledgeScreenshotAsync(string requestId);

    /// <summary>Most recent commands for a device, newest first (for the Devices UI).</summary>
    Task<List<TblDeviceCommand>> GetHistoryAsync(string deviceId, int take = 15);
}

public class DeviceCommandService : IDeviceCommandService
{
    private readonly IDirectorDBContext _db;
    private readonly ILogger<DeviceCommandService> _logger;
    private readonly IPlayerWakeNotifier _wakeNotifier;
    private readonly DXSuite.Shared.Multitenancy.ITenantContext _tenantContext;

    // Per-type delivery expiry: a device offline past this window must not execute the
    // command when it returns (e.g. a Friday power-off landing Monday morning). A cache
    // flush is content-safe at any age, so it waits out a long outage.
    private static readonly Dictionary<string, TimeSpan> Expiry = new()
    {
        [DeviceCommandTypes.Cec] = TimeSpan.FromMinutes(15),
        [DeviceCommandTypes.Screenshot] = TimeSpan.FromMinutes(15),
        [DeviceCommandTypes.FlushCache] = TimeSpan.FromHours(24)
    };

    private static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(30);

    public DeviceCommandService(
        IDirectorDBContext db,
        ILogger<DeviceCommandService> logger,
        IPlayerWakeNotifier wakeNotifier,
        DXSuite.Shared.Multitenancy.ITenantContext tenantContext)
    {
        _db = db;
        _logger = logger;
        _wakeNotifier = wakeNotifier;
        _tenantContext = tenantContext;
    }

    public Task QueueCecAsync(string deviceId, string action, string? output, string? issuedBy)
        => QueueAsync(deviceId, DeviceCommandTypes.Cec,
            JsonSerializer.Serialize(new CecCommandPayload { Action = action, Output = output }), null, issuedBy);

    public async Task<string> QueueScreenshotAsync(string deviceId, string? issuedBy)
    {
        var requestId = Guid.NewGuid().ToString("N");
        await QueueAsync(deviceId, DeviceCommandTypes.Screenshot, null, requestId, issuedBy);
        return requestId;
    }

    public Task QueueFlushCacheAsync(string deviceId, string? issuedBy)
        => QueueAsync(deviceId, DeviceCommandTypes.FlushCache, null, null, issuedBy);

    private async Task QueueAsync(string deviceId, string commandType, string? payloadJson, string? requestId, string? issuedBy)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        var now = DateTime.UtcNow;

        // Latest wins, never stacks: an undelivered command of the same type is superseded
        var pending = await _db.TblDeviceCommands
            .Where(c => c.DeviceId == deviceId && c.CommandType == commandType && c.Status == DeviceCommandStatus.Queued)
            .ToListAsync();
        foreach (var previous in pending)
            previous.Status = DeviceCommandStatus.Superseded;

        // Opportunistic retention so the table stays bounded without a sweeper
        var retentionCutoff = now - HistoryRetention;
        var stale = await _db.TblDeviceCommands
            .Where(c => c.DeviceId == deviceId && c.IssuedAt < retentionCutoff)
            .ToListAsync();
        if (stale.Count > 0)
            _db.TblDeviceCommands.RemoveRange(stale);

        _db.TblDeviceCommands.Add(new TblDeviceCommand
        {
            DeviceId = deviceId,
            CommandType = commandType,
            PayloadJson = payloadJson,
            RequestId = requestId,
            IssuedBy = issuedBy,
            IssuedAt = now,
            ExpiresAt = now + Expiry[commandType],
            Status = DeviceCommandStatus.Queued
        });
        await _db.SaveChangesAsync();

        // Wake the device so the command is picked up on an immediate poll instead of
        // waiting out the cadence; delivery still rides the normal poll response
        _wakeNotifier.NotifyDevice(_tenantContext.TenantId, deviceId);

        _logger.LogInformation("{CommandType} command queued for device {DeviceId}{Superseded}",
            commandType, deviceId, pending.Count > 0 ? $" (superseded {pending.Count})" : string.Empty);
    }

    public async Task<List<TblDeviceCommand>> DequeuePendingAsync(string deviceId)
    {
        var queued = await _db.TblDeviceCommands
            .Where(c => c.DeviceId == deviceId && c.Status == DeviceCommandStatus.Queued)
            .ToListAsync();
        if (queued.Count == 0)
            return queued;

        var now = DateTime.UtcNow;
        var delivered = new List<TblDeviceCommand>();
        foreach (var command in queued)
        {
            if (command.ExpiresAt < now)
            {
                command.Status = DeviceCommandStatus.Expired;
                _logger.LogWarning("{CommandType} command for device {DeviceId} expired before delivery (issued {IssuedAt})",
                    command.CommandType, deviceId, command.IssuedAt);
            }
            else
            {
                command.Status = DeviceCommandStatus.Delivered;
                command.DeliveredAt = now;
                delivered.Add(command);
            }
        }
        await _db.SaveChangesAsync();
        return delivered;
    }

    public async Task<string?> ResolveScreenshotUploadAsync(string requestId)
    {
        if (string.IsNullOrEmpty(requestId))
            return null;

        var cutoff = DateTime.UtcNow - UploadWindow;
        var command = await _db.TblDeviceCommands
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RequestId == requestId
                && (c.Status == DeviceCommandStatus.Delivered || c.Status == DeviceCommandStatus.Acknowledged)
                && c.DeliveredAt >= cutoff);
        return command?.DeviceId;
    }

    public async Task AcknowledgeScreenshotAsync(string requestId)
    {
        if (string.IsNullOrEmpty(requestId))
            return;

        var command = await _db.TblDeviceCommands
            .FirstOrDefaultAsync(c => c.RequestId == requestId && c.Status == DeviceCommandStatus.Delivered);
        if (command != null)
        {
            command.Status = DeviceCommandStatus.Acknowledged;
            command.AcknowledgedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public Task<List<TblDeviceCommand>> GetHistoryAsync(string deviceId, int take = 15)
        => _db.TblDeviceCommands
            .AsNoTracking()
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.IssuedAt)
            .Take(take)
            .ToListAsync();
}
