using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

/// <summary>
/// Periodically queues a screenshot request for each active Engage player so the Screenshots card view
/// stays reasonably current without an operator clicking "Capture" on every device. Low frequency by
/// design — captures are support-grade. Disabled unless <c>Screenshots:RefreshIntervalMinutes</c> is set
/// to a positive value; the per-device request still flows through the normal capture path (device grabs
/// it on its next sync and uploads a downscaled image).
/// </summary>
public class ScreenshotRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly IConfiguration _config;
    private readonly ILogger<ScreenshotRefreshService> _logger;

    // Only request from players that have checked in recently — no point queuing for offline devices.
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(15);

    public ScreenshotRefreshService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockService lockService,
        IConfiguration config,
        ILogger<ScreenshotRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockService = lockService;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _config.GetValue<int>("Screenshots:RefreshIntervalMinutes");
        if (intervalMinutes <= 0)
        {
            _logger.LogInformation("ScreenshotRefreshService disabled (Screenshots:RefreshIntervalMinutes not set)");
            return;
        }

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        _logger.LogInformation("ScreenshotRefreshService started; refreshing every {Minutes} min", intervalMinutes);

        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // One instance per cluster does the refresh.
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "screenshot-refresh-service-lock", TimeSpan.FromMinutes(5), stoppingToken);

                if (distLock != null)
                {
                    await QueueForActiveDevicesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScreenshotRefreshService cycle");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }

        _logger.LogInformation("ScreenshotRefreshService stopped");
    }

    private async Task QueueForActiveDevicesAsync(CancellationToken ct)
    {
        // IDbContextFactory is scoped in this host, so resolve it inside a scope (this is a singleton service).
        using var scope = _scopeFactory.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DirectorDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - OnlineWindow;

        // Active Engage players = assigned to a location, recently heart-beating, not an Ink content folder.
        var deviceIds = await db.TblDevices
            .AsNoTracking()
            .Where(d => d.LocationId != null && d.Heartbeat > cutoff && !d.DeviceId.StartsWith("inkf-"))
            .Select(d => d.DeviceId)
            .ToListAsync(ct);

        var deviceCommands = scope.ServiceProvider.GetRequiredService<IDeviceCommandService>();
        foreach (var deviceId in deviceIds)
            await deviceCommands.QueueScreenshotAsync(deviceId, "periodic-refresh");

        if (deviceIds.Count > 0)
            _logger.LogDebug("ScreenshotRefreshService queued {Count} screenshot requests", deviceIds.Count);
    }
}
