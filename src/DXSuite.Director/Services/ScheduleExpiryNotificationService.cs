using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// Hourly background service that sends "schedule-expiring" notifications
/// when schedule events are within 24 hours of ending.
/// Deduplicates by checking if a notification with the same ReferenceId already exists.
/// </summary>
public class ScheduleExpiryNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<ScheduleExpiryNotificationService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public ScheduleExpiryNotificationService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockService lockService,
        ILogger<ScheduleExpiryNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockService = lockService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            using var distLock = await _lockService.TryAcquireLockAsync("schedule-expiry-notifications", TimeSpan.FromMinutes(5), stoppingToken);
            if (distLock == null) continue;

            try
            {
                await CheckExpiringScehdules(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in schedule expiry notification cycle");
            }
        }
    }

    private async Task CheckExpiringScehdules(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<DXSuite.Shared.Services.INotificationService>();

        // tblScheduleEvent.EndDate is a wall-clock bound, so the comparison is wall clock to wall
        // clock and the host's local time is the right side of it. Deliberately not resolved per
        // device: this is a tick sweep with no device in hand, and one display can span zones.
        var now = DateTime.Now;
        var cutoff = now.AddHours(24);

        // Find schedule events ending within the next 24 hours
        var expiringEvents = await db.TblScheduleEvents
            .AsNoTracking()
            .Where(e => e.EndDate > now && e.EndDate <= cutoff)
            .Select(e => new { e.ScheduleEventId, e.Display.LocationId, e.EndDate })
            .ToListAsync(ct);

        if (expiringEvents.Count == 0) return;

        // Check which events already have notifications
        var eventIds = expiringEvents.Select(e => e.ScheduleEventId).ToList();
        var existingNotificationRefs = await db.TblNotifications
            .AsNoTracking()
            .Where(n => n.NotificationType == "schedule-expiring" && n.ReferenceId != null && eventIds.Contains(n.ReferenceId.Value))
            .Select(n => n.ReferenceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var newEvents = expiringEvents.Where(e => !existingNotificationRefs.Contains(e.ScheduleEventId)).ToList();
        if (newEvents.Count == 0) return;

        // Get GlobalAdmin users to notify
        var adminUserIds = await db.TblUsers
            .AsNoTracking()
            .Where(u => u.Role.RoleName == "GlobalAdmin")
            .Select(u => u.UserId)
            .ToListAsync(ct);

        if (adminUserIds.Count == 0) return;

        int sent = 0;
        foreach (var evt in newEvents)
        {
            if (ct.IsCancellationRequested) break;

            await notificationService.CreateBulkNotificationsAsync(
                adminUserIds,
                "Schedule event expiring soon",
                $"A schedule event ends at {evt.EndDate:MMM d, h:mm tt}.",
                "schedule-expiring",
                evt.ScheduleEventId,
                "/");

            sent++;
        }

        if (sent > 0)
        {
            _logger.LogInformation("Sent {Count} schedule expiry notifications", sent);
        }
    }
}
