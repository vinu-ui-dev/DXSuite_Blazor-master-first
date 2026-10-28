using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Pure-logic evaluator for time-based schedule windows.
/// Determines whether a schedule is active at a given point in time by ANDing date range,
/// time-of-day window, and day-of-week filters.
/// </summary>
public class ScheduleEvaluator
{
    /// <summary>
    /// Evaluates whether a schedule window is active at the given timestamp.
    /// All criteria are ANDed: date range, time window, and day-of-week must all pass.
    /// </summary>
    public bool IsActive(IScheduleWindow schedule, DateTime now)
    {
        if (!IsDateInRange(schedule, now))
            return false;

        if (!IsDayAllowed(schedule, now.DayOfWeek))
            return false;

        if (!IsTimeInWindow(schedule, TimeOnly.FromDateTime(now)))
            return false;

        return true;
    }

    /// <summary>
    /// Date range check. Null bounds are treated as unbounded.
    /// Neither bound = always in range.
    /// </summary>
    private static bool IsDateInRange(IScheduleWindow schedule, DateTime now)
    {
        if (schedule.StartDate.HasValue && now.Date < schedule.StartDate.Value.Date)
            return false;

        if (schedule.EndDate.HasValue && now.Date > schedule.EndDate.Value.Date)
            return false;

        return true;
    }

    /// <summary>
    /// Day-of-week check. All flags false = every day allowed.
    /// </summary>
    private static bool IsDayAllowed(IScheduleWindow schedule, DayOfWeek day)
    {
        bool anyDaySet = schedule.Sunday || schedule.Monday || schedule.Tuesday ||
                         schedule.Wednesday || schedule.Thursday || schedule.Friday || schedule.Saturday;

        if (!anyDaySet)
            return true;

        return day switch
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

    /// <summary>
    /// Time-of-day check. Both start and end must be provided together.
    /// StartTime > EndTime indicates overnight rollover (e.g., 22:00-06:00).
    /// No time bounds = always in window.
    /// </summary>
    private static bool IsTimeInWindow(IScheduleWindow schedule, TimeOnly currentTime)
    {
        if (!schedule.StartTime.HasValue || !schedule.EndTime.HasValue)
            return true;

        var start = schedule.StartTime.Value;
        var end = schedule.EndTime.Value;

        if (start <= end)
        {
            // Normal window: 08:00-17:00
            return currentTime >= start && currentTime < end;
        }
        else
        {
            // Overnight rollover: 22:00-06:00
            return currentTime >= start || currentTime < end;
        }
    }
}
