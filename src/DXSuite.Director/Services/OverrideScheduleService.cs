using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

public class OverrideScheduleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverrideScheduleService> _logger;
    private readonly IOverrideService _overrideService;
    private readonly IDistributedLockService _lockService;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public OverrideScheduleService(
        IServiceScopeFactory scopeFactory,
        ILogger<OverrideScheduleService> logger,
        IOverrideService overrideService,
        IDistributedLockService lockService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _overrideService = overrideService;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Override Schedule Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "override-schedule-service-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    await CheckAndUpdateOverrideStatuses();
                }
                else
                {
                    _logger.LogDebug("Another instance is handling override schedules, skipping this cycle");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while monitoring override schedules");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("Override Schedule Service stopped.");
    }

    private async Task CheckAndUpdateOverrideStatuses()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var currentDateTime = DateTime.Now;
        var currentTime = TimeOnly.FromDateTime(currentDateTime);
        var currentDate = DateOnly.FromDateTime(currentDateTime);
        var currentDayOfWeek = currentDateTime.DayOfWeek;

        _logger.LogDebug("Checking override schedules at {CurrentTime} on {DayOfWeek}",
            currentTime.ToString("HH:mm:ss"), currentDayOfWeek);

        // Get all scheduled overrides
        var scheduledOverrides = await dbContext.TblOverrides
            .Where(o => o.IsScheduled)
            .ToListAsync();

        foreach (var over in scheduledOverrides)
        {
            try
            {
                var scheduleIsActive = IsScheduleActive(over, currentDateTime, currentTime, currentDayOfWeek);
                var previousState = over.LastScheduleState;

                // Detect state transitions - only act on boundaries
                // This allows manual toggles to work between scheduled start/end times

                if (scheduleIsActive && previousState != true)
                {
                    // TRANSITION: Schedule became active (start time reached)
                    _logger.LogInformation(
                        "Override '{OverrideName}' (ID: {OverrideId}) - Schedule START triggered at {Time}",
                        over.OverrideName, over.OverrideId, currentTime);

                    await _overrideService.ActivateOverrideAsync(over.OverrideId, "SCHEDULE", "System", null);

                    // Update tracking fields
                    over.LastScheduleState = true;
                    over.LastScheduleActivation = currentDateTime;
                    await dbContext.SaveChangesAsync();
                }
                else if (!scheduleIsActive && previousState == true)
                {
                    // TRANSITION: Schedule became inactive (end time reached)
                    _logger.LogInformation(
                        "Override '{OverrideName}' (ID: {OverrideId}) - Schedule END triggered at {Time}",
                        over.OverrideName, over.OverrideId, currentTime);

                    await _overrideService.DeactivateOverrideAsync(over.OverrideId, "SCHEDULE", "System", null);

                    // Update tracking fields
                    over.LastScheduleState = false;
                    over.LastScheduleDeactivation = currentDateTime;
                    await dbContext.SaveChangesAsync();
                }
                else if (previousState == null)
                {
                    // First evaluation - just record the current state without triggering
                    // This prevents spurious triggers on service startup
                    _logger.LogDebug(
                        "Override '{OverrideName}' (ID: {OverrideId}) - Initial schedule state recorded: {State}",
                        over.OverrideName, over.OverrideId, scheduleIsActive);

                    over.LastScheduleState = scheduleIsActive;
                    await dbContext.SaveChangesAsync();
                }
                // else: No transition, do nothing - allows manual control
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing schedule for override {OverrideId}", over.OverrideId);
            }
        }
    }

    private bool IsScheduleActive(TblOverride over, DateTime currentDateTime, TimeOnly currentTime, DayOfWeek currentDayOfWeek)
    {
        // Check date range
        if (over.StartDate.HasValue && currentDateTime.Date < over.StartDate.Value.Date)
            return false;

        if (over.EndDate.HasValue && currentDateTime.Date > over.EndDate.Value.Date)
            return false;

        // Check day of week
        if (!IsDayActive(over, currentDayOfWeek))
            return false;

        // Check time range
        if (!over.StartTime.HasValue && !over.EndTime.HasValue)
            return true; // No time restrictions, active all day

        return ScheduleTime.IsWithinWindow(currentTime, over.StartTime, over.EndTime);
    }

    private bool IsDayActive(TblOverride over, DayOfWeek dayOfWeek)
    {
        // If no days are explicitly selected, assume all days are active
        bool anyDaySelected = over.Sunday || over.Monday || over.Tuesday ||
                             over.Wednesday || over.Thursday || over.Friday || over.Saturday;

        if (!anyDaySelected)
            return true;

        return dayOfWeek switch
        {
            DayOfWeek.Sunday => over.Sunday,
            DayOfWeek.Monday => over.Monday,
            DayOfWeek.Tuesday => over.Tuesday,
            DayOfWeek.Wednesday => over.Wednesday,
            DayOfWeek.Thursday => over.Thursday,
            DayOfWeek.Friday => over.Friday,
            DayOfWeek.Saturday => over.Saturday,
            _ => false
        };
    }
}
