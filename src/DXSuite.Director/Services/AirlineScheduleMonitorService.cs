using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DXSuite.Aviation.Entities;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

public class AirlineScheduleMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AirlineScheduleMonitorService> _logger;
    private readonly AirlineSchedulingConfig _airlineConfig;
    private readonly IDistributedLockService _lockService;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // Check every minute

    public AirlineScheduleMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<AirlineScheduleMonitorService> logger,
        IOptions<AirlineSchedulingConfig> airlineConfig,
        IDistributedLockService lockService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _airlineConfig = airlineConfig.Value;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Airline Schedule Monitor Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "airline-schedule-monitor-service-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    await CheckAndUpdateAirlineStatuses();
                }
                else
                {
                    _logger.LogDebug("Another instance is handling airline schedules, skipping this cycle");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is shutting down
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while monitoring airline schedules");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("Airline Schedule Monitor Service stopped.");
    }

    private async Task CheckAndUpdateAirlineStatuses()
    {
        using var scope = _scopeFactory.CreateScope();
        var directorContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // AviationDbContext is only registered when the Aviation module is included in the build.
        // Skip silently when running in a deployment that excludes Aviation (e.g. NetJets-only).
        var aviationContext = scope.ServiceProvider.GetService<AviationDbContext>();
        if (aviationContext is null)
        {
            _logger.LogDebug("Aviation module is not available — airline schedule monitoring skipped.");
            return;
        }

        var currentDateTime = DateTime.Now;
        var currentTime = TimeOnly.FromDateTime(currentDateTime);
        var currentDate = DateOnly.FromDateTime(currentDateTime);
        var currentDayOfWeek = currentDateTime.DayOfWeek;

        _logger.LogInformation("Checking airline schedules at {CurrentTime} on {DayOfWeek}",
            currentTime.ToString("HH:mm:ss"), currentDayOfWeek);

        // Get all active airline schedules with flexible date filtering
        var activeSchedules = await directorContext.TblScheduleEvents
            .Where(se => se.ScheduleEventTypeId == 2 && !string.IsNullOrEmpty(se.AirlineCode))
            .Where(se =>
                // No dates set = always active
                (se.StartDate == null && se.EndDate == null) ||
                // Only start date set = active from start date forward
                (se.StartDate != null && se.EndDate == null && se.StartDate <= currentDateTime) ||
                // Only end date set = active until end date
                (se.StartDate == null && se.EndDate != null && se.EndDate >= currentDateTime) ||
                // Both dates set = active within range
                (se.StartDate != null && se.EndDate != null && se.StartDate <= currentDateTime && se.EndDate >= currentDateTime))
            .ToListAsync();

        _logger.LogInformation("Found {ScheduleCount} active airline schedules", activeSchedules.Count);

        // Group schedules by airline code
        var airlineSchedules = activeSchedules
            .GroupBy(s => s.AirlineCode)
            .ToDictionary(g => g.Key!, g => g.ToList());

        // Get all airlines that need monitoring (either location enabled)
        var airlinesWithSchedules = airlineSchedules.Keys.ToList();
        var airlines = await aviationContext.TblAirlines
            .Where(a => (a.Tpenabled1 == true || a.Tpenabled2 == true) && airlinesWithSchedules.Contains(a.AirlineCode))
            .ToListAsync();

        var statusUpdates = new List<(string AirlineCode, int Location, int NewStatus, int OldStatus)>();

        foreach (var airline in airlines)
        {
            if (!airlineSchedules.TryGetValue(airline.AirlineCode, out var schedules))
                continue;

            _logger.LogInformation("Evaluating {AirlineCode} with {ScheduleCount} schedules",
                airline.AirlineCode, schedules.Count);

            // Process Location 1 (Terminal/1st Floor) if enabled
            if (airline.Tpenabled1 == true)
            {
                var currentStatus1 = airline.CarrierStatus1 ?? 2; // Default to closed
                var location1Schedules = schedules.Where(s => s.DisplayId == _airlineConfig.TerminalDisplayId).ToList();
                var (shouldUpdate1, newStatus1, transitionType1) = ShouldUpdateForTransition(location1Schedules, currentDateTime, currentTime, currentDayOfWeek, currentStatus1);

                if (shouldUpdate1)
                {
                    airline.CarrierStatus1 = newStatus1;
                    statusUpdates.Add((airline.AirlineCode, 1, newStatus1, currentStatus1));

                    _logger.LogInformation(
                        "Airline {AirlineCode} ({CarrierName}) Location 1 {TransitionType} transition: {OldStatus} -> {NewStatus}",
                        airline.AirlineCode, airline.CarrierName, transitionType1,
                        currentStatus1 == 1 ? "OPEN" : "CLOSED",
                        newStatus1 == 1 ? "OPEN" : "CLOSED");
                }
                else
                {
                    _logger.LogInformation("No transition trigger for {AirlineCode} Location 1 (current status: {Status}) - {ScheduleCount} schedules",
                        airline.AirlineCode, currentStatus1 == 1 ? "OPEN" : "CLOSED", location1Schedules.Count);
                }
            }

            // Process Location 2 (Garage/2nd Floor) if enabled
            if (airline.Tpenabled2 == true)
            {
                var currentStatus2 = airline.CarrierStatus2 ?? 2; // Default to closed
                var location2Schedules = schedules.Where(s => s.DisplayId == _airlineConfig.GarageDisplayId).ToList();
                var (shouldUpdate2, newStatus2, transitionType2) = ShouldUpdateForTransition(location2Schedules, currentDateTime, currentTime, currentDayOfWeek, currentStatus2);

                if (shouldUpdate2)
                {
                    airline.CarrierStatus2 = newStatus2;
                    statusUpdates.Add((airline.AirlineCode, 2, newStatus2, currentStatus2));

                    _logger.LogInformation(
                        "Airline {AirlineCode} ({CarrierName}) Location 2 {TransitionType} transition: {OldStatus} -> {NewStatus}",
                        airline.AirlineCode, airline.CarrierName, transitionType2,
                        currentStatus2 == 1 ? "OPEN" : "CLOSED",
                        newStatus2 == 1 ? "OPEN" : "CLOSED");
                }
                else
                {
                    _logger.LogInformation("No transition trigger for {AirlineCode} Location 2 (current status: {Status}) - {ScheduleCount} schedules",
                        airline.AirlineCode, currentStatus2 == 1 ? "OPEN" : "CLOSED", location2Schedules.Count);
                }
            }
        }

        // Save changes if any status updates occurred
        if (statusUpdates.Any())
        {
            await aviationContext.SaveChangesAsync();
            _logger.LogInformation("Updated {Count} airline location statuses", statusUpdates.Count);

            // Log details for each status update
            foreach (var update in statusUpdates)
            {
                _logger.LogInformation(
                    "Status update: {AirlineCode} Location {Location}: {OldStatus} -> {NewStatus}",
                    update.AirlineCode, update.Location,
                    update.OldStatus == 1 ? "OPEN" : "CLOSED",
                    update.NewStatus == 1 ? "OPEN" : "CLOSED");
            }
        }
    }

    private (bool ShouldUpdate, int NewStatus, string TransitionType) ShouldUpdateForTransition(
        List<TblScheduleEvent> schedules,
        DateTime currentDateTime,
        TimeOnly currentTime,
        DayOfWeek currentDayOfWeek,
        int currentStatus)
    {
        const int TransitionWindowSeconds = 60;

        foreach (var schedule in schedules)
        {
            // Check if schedule applies to today
            if (!IsDayIncludedInSchedule(schedule, currentDayOfWeek))
                continue;

            var (startTime, endTime) = GetEffectiveScheduleTimes(schedule);

            // Check for OPEN transition (start of schedule)
            if (currentStatus == 2) // Currently closed
            {
                if (IsWithinTransitionWindow(currentTime, startTime, TransitionWindowSeconds))
                {
                    return (true, 1, "OPEN");
                }
            }

            // Check for CLOSE transition (end of schedule)
            if (currentStatus == 1) // Currently open
            {
                if (IsWithinTransitionWindow(currentTime, endTime, TransitionWindowSeconds))
                {
                    return (true, 2, "CLOSE");
                }
            }
        }

        return (false, currentStatus, "");
    }

    private (TimeOnly StartTime, TimeOnly EndTime) GetEffectiveScheduleTimes(TblScheduleEvent schedule)
    {
        // If no times specified, use all-day (midnight to 11:59 PM)
        var startTime = schedule.StartTime ?? new TimeOnly(0, 0, 0);
        var endTime = schedule.EndTime ?? new TimeOnly(23, 59, 59);

        return (startTime, endTime);
    }

    private bool IsWithinTransitionWindow(TimeOnly currentTime, TimeOnly transitionTime, int windowSeconds)
    {
        var windowTimeSpan = TimeSpan.FromSeconds(windowSeconds);
        var transitionStart = transitionTime.Add(-windowTimeSpan);
        var transitionEnd = transitionTime.Add(windowTimeSpan);

        // Handle case where transition window crosses midnight
        if (transitionStart < TimeOnly.MinValue)
        {
            // Window starts yesterday, check if current time is after midnight within window
            var adjustedStart = transitionStart.Add(TimeSpan.FromDays(1));
            return currentTime >= adjustedStart || currentTime <= transitionEnd;
        }
        else if (transitionEnd > TimeOnly.MaxValue)
        {
            // Window extends past midnight, check if current time is before midnight within window
            var adjustedEnd = transitionEnd.Add(-TimeSpan.FromDays(1));
            return currentTime >= transitionStart || currentTime <= adjustedEnd;
        }
        else
        {
            // Normal case - window doesn't cross midnight
            return currentTime >= transitionStart && currentTime <= transitionEnd;
        }
    }

    private bool IsDayIncludedInSchedule(TblScheduleEvent schedule, DayOfWeek dayOfWeek)
    {
        // If no days are selected, assume all days are active
        bool anyDaySelected = schedule.Sunday || schedule.Monday || schedule.Tuesday ||
                             schedule.Wednesday || schedule.Thursday || schedule.Friday || schedule.Saturday;

        if (!anyDaySelected)
        {
            return true; // No days selected = applies to every day
        }

        return dayOfWeek switch
        {
            DayOfWeek.Sunday => schedule.Sunday,
            DayOfWeek.Monday => schedule.Monday,
            DayOfWeek.Tuesday => schedule.Tuesday,
            DayOfWeek.Wednesday => schedule.Wednesday,
            DayOfWeek.Thursday => schedule.Thursday,
            DayOfWeek.Friday => schedule.Friday,
            DayOfWeek.Saturday => schedule.Saturday,
            _ => false
        };
    }
}
