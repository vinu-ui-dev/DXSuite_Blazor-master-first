using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Service for managing automatic device healing (power cycling unresponsive LG displays)
/// </summary>
public class AutoHealService : IAutoHealService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILgDisplayControlService _lgDisplayControl;
    private readonly ILogger<AutoHealService> _logger;

    // Configuration
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(30);

    public AutoHealService(
        IServiceScopeFactory scopeFactory,
        ILgDisplayControlService lgDisplayControl,
        ILogger<AutoHealService> logger)
    {
        _scopeFactory = scopeFactory;
        _lgDisplayControl = lgDisplayControl;
        _logger = logger;
    }

    // How long a device must be in background state before triggering heal
    private static readonly TimeSpan BackgroundThreshold = TimeSpan.FromMinutes(2);

    public async Task CheckAndHealDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== AUTO-HEAL CHECK STARTING ===");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Track the configured poll cadence: a slower fleet pace must not trigger heals
        var syncSettings = await scope.ServiceProvider
            .GetRequiredService<IPlayerSyncSettingsService>()
            .GetAsync();

        var now = DateTime.UtcNow;
        var offlineThreshold = now - syncSettings.OnlineThreshold;
        var backgroundThreshold = now - BackgroundThreshold;

        _logger.LogInformation("Current UTC: {Now}, OfflineThreshold: {OfflineThreshold}, BackgroundThreshold: {BackgroundThreshold}",
            now, offlineThreshold, backgroundThreshold);

        // Log all auto-heal enabled devices for debugging
        var allAutoHealDevices = await db.TblDevices
            .Where(d => d.AutoHealEnabled)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} device(s) with AutoHealEnabled=true", allAutoHealDevices.Count);

        foreach (var d in allAutoHealDevices)
        {
            var secondsOffline = (now - d.Heartbeat).TotalSeconds;
            var isOffline = d.Heartbeat < offlineThreshold;
            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(d.ExtendedData);
            var inputStatus = extData?.InputStatus?.Status ?? "null";
            _logger.LogInformation(
                "  Device {DeviceId}: Heartbeat={Heartbeat} (UTC), SecondsOffline={Seconds:F0}, IsOffline={IsOffline}, InputStatus={InputStatus}",
                d.DeviceId, d.Heartbeat, secondsOffline, isOffline, inputStatus);
        }

        // Find devices with auto-heal enabled that need attention:
        // 1. Offline (no heartbeat) - app crashed or display frozen
        // 2. Background input status for too long - wrong input source selected
        // Parse ExtendedData and filter in memory since we can't query JSON directly
        var devicesNeedingHeal = allAutoHealDevices
            .Where(d =>
            {
                // Offline devices always need attention
                if (d.Heartbeat < offlineThreshold)
                    return true;

                // Check input status from ExtendedData
                var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(d.ExtendedData);
                var inputStatus = extData?.InputStatus;
                if (inputStatus?.Status == "background" && inputStatus.ChangedAt.HasValue)
                {
                    return inputStatus.ChangedAt.Value < backgroundThreshold;
                }

                return false;
            })
            .ToList();

        if (devicesNeedingHeal.Count == 0)
        {
            _logger.LogInformation("No devices need healing at this time");
            return;
        }

        // Separate for logging
        var offlineDevices = devicesNeedingHeal.Where(d => d.Heartbeat < offlineThreshold).ToList();
        var wrongInputDevices = devicesNeedingHeal
            .Where(d =>
            {
                if (d.Heartbeat < offlineThreshold) return false;
                var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(d.ExtendedData);
                return extData?.InputStatus?.Status == "background";
            })
            .ToList();

        if (offlineDevices.Count > 0)
            _logger.LogInformation("Found {Count} offline device(s) with auto-heal enabled", offlineDevices.Count);
        if (wrongInputDevices.Count > 0)
            _logger.LogInformation("Found {Count} device(s) with wrong input source", wrongInputDevices.Count);

        foreach (var device in devicesNeedingHeal)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Check cooldown
            var lastAttempt = await GetLastHealAttemptAsync(device.DeviceId);
            if (lastAttempt.HasValue && (now - lastAttempt.Value) < CooldownPeriod)
            {
                _logger.LogDebug("Device {DeviceId} is in cooldown until {CooldownEnd}",
                    device.DeviceId, lastAttempt.Value + CooldownPeriod);
                continue;
            }

            // Determine reason for heal
            var reason = device.Heartbeat < offlineThreshold ? "offline" : "wrong_input";

            // Attempt heal
            var result = await AttemptHealAsync(device.DeviceId, triggeredBy: $"AutoHealService:{reason}", cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Auto-heal successful for device {DeviceId} ({Reason}) at {IpAddress}",
                    device.DeviceId, reason, device.Ipaddress);
            }
            else
            {
                _logger.LogWarning("Auto-heal failed for device {DeviceId} ({Reason}): {Error}",
                    device.DeviceId, reason, result.ErrorMessage);
            }
        }
    }

    public async Task<AutoHealResult> AttemptHealAsync(string deviceId, string? triggeredBy = null, CancellationToken cancellationToken = default)
    {
        var result = new AutoHealResult { DeviceId = deviceId };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Get device
        var device = await db.TblDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
        if (device == null)
        {
            result.DeviceNotFound = true;
            result.ErrorMessage = "Device not found";
            return result;
        }

        result.IpAddress = device.Ipaddress;

        // Check if auto-heal is disabled (for manual triggers, we still proceed but log it)
        if (!device.AutoHealEnabled && triggeredBy != "Manual")
        {
            result.AutoHealDisabled = true;
            result.ErrorMessage = "Auto-heal is disabled for this device";
            return result;
        }

        // Check cooldown (skip for manual triggers)
        if (triggeredBy != "Manual")
        {
            var lastAttempt = await GetLastHealAttemptForDeviceAsync(db, deviceId);
            if (lastAttempt.HasValue && (DateTime.UtcNow - lastAttempt.Value) < CooldownPeriod)
            {
                result.WasInCooldown = true;
                result.ErrorMessage = $"Device is in cooldown until {lastAttempt.Value + CooldownPeriod:HH:mm:ss}";
                return result;
            }
        }

        // Get event type ID for AUTO_HEAL
        var eventType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "AUTO_HEAL", cancellationToken);
        var infoSeverity = await db.TblEventSeverities.FirstOrDefaultAsync(s => s.SeverityName == "INFO", cancellationToken);
        var warningSeverity = await db.TblEventSeverities.FirstOrDefaultAsync(s => s.SeverityName == "WARNING", cancellationToken);

        if (eventType == null || infoSeverity == null || warningSeverity == null)
        {
            _logger.LogWarning("Event type or severity not found in database. Run UnifiedEventLog_Schema.sql first.");
            // Continue anyway, we'll handle the null case
        }

        // Attempt power cycle
        _logger.LogInformation("Attempting auto-heal for device {DeviceId} at {IpAddress}", deviceId, device.Ipaddress);
        result.Action = "POWER_CYCLE";

        var lgResult = await _lgDisplayControl.PowerCycleAsync(device.Ipaddress, setId: 1, delayMs: 3000);

        result.Success = lgResult.Success;
        result.Response = lgResult.Response;
        result.ErrorMessage = lgResult.ErrorMessage;
        result.ElapsedMs = lgResult.ElapsedMs;

        // Log the event
        var eventLog = new TblEventLog
        {
            EventSource = "AutoHealService",
            EventTypeId = eventType?.EventTypeId ?? 1,
            EventTitle = result.Success ? "Auto-Heal Successful" : "Auto-Heal Failed",
            EventMessage = result.Success
                ? $"Power cycle completed successfully for device at {device.Ipaddress}"
                : $"Power cycle failed: {result.ErrorMessage}",
            EventSeverityId = result.Success ? (infoSeverity?.EventSeverityId ?? 1) : (warningSeverity?.EventSeverityId ?? 2),
            DeviceId = deviceId,
            Success = result.Success,
            CreateDate = DateTime.UtcNow,
            Details = JsonSerializer.Serialize(new
            {
                action = result.Action,
                ipAddress = device.Ipaddress,
                triggeredBy = triggeredBy ?? "AutoHealService",
                command = lgResult.Command,
                response = lgResult.Response,
                elapsedMs = lgResult.ElapsedMs,
                errorMessage = lgResult.ErrorMessage
            })
        };

        db.TblEventLogs.Add(eventLog);
        await db.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task<DateTime?> GetLastHealAttemptAsync(string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        return await GetLastHealAttemptForDeviceAsync(db, deviceId);
    }

    private async Task<DateTime?> GetLastHealAttemptForDeviceAsync(IDirectorDBContext db, string deviceId)
    {
        var eventType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "AUTO_HEAL");
        if (eventType == null)
            return null;

        var lastAttempt = await db.TblEventLogs
            .Where(e => e.DeviceId == deviceId && e.EventTypeId == eventType.EventTypeId)
            .OrderByDescending(e => e.CreateDate)
            .Select(e => (DateTime?)e.CreateDate)
            .FirstOrDefaultAsync();

        return lastAttempt;
    }

    public async Task<AutoHealStats> GetStatsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var syncSettings = await scope.ServiceProvider
            .GetRequiredService<IPlayerSyncSettingsService>()
            .GetAsync();

        var now = DateTime.Now;
        var offlineThreshold = now - syncSettings.OnlineThreshold;

        var eventType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "AUTO_HEAL");

        var stats = new AutoHealStats
        {
            DevicesWithAutoHealEnabled = await db.TblDevices.CountAsync(d => d.AutoHealEnabled),
            DevicesCurrentlyOffline = await db.TblDevices.CountAsync(d => d.AutoHealEnabled && d.Heartbeat < offlineThreshold)
        };

        if (eventType != null)
        {
            var events = db.TblEventLogs.Where(e => e.EventTypeId == eventType.EventTypeId);

            stats.TotalAttempts = await events.CountAsync();
            stats.SuccessfulAttempts = await events.CountAsync(e => e.Success == true);
            stats.FailedAttempts = await events.CountAsync(e => e.Success == false);
            stats.LastAttemptTime = await events
                .OrderByDescending(e => e.CreateDate)
                .Select(e => (DateTime?)e.CreateDate)
                .FirstOrDefaultAsync();
        }

        return stats;
    }
}
