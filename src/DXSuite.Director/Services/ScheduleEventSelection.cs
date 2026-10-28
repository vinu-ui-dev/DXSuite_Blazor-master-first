using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Picks the schedule event active at a local wall-clock moment. One rule, used by the display path
/// both when it hydrates a publication and when it resolves a delivery out of the cache, so the two
/// can never drift into disagreeing about which event is on.
/// </summary>
public static class ScheduleEventSelection
{
    /// <summary>
    /// The last matching non-default event wins, which is the long-standing behaviour: events are
    /// authored in priority order and the later row is the more specific one. Falls back to the
    /// default event when nothing matches.
    /// </summary>
    public static T? PickActive<T>(IEnumerable<T> events, DateTime localNow, Func<T, bool> isDefault)
        where T : IScheduleWindow
    {
        T? active = default;

        foreach (var candidate in events)
        {
            if (isDefault(candidate)) continue;
            if (!IsActiveAt(candidate, localNow)) continue;

            active = candidate;
        }

        if (active is not null) return active;

        return events.FirstOrDefault(isDefault);
    }

    public static bool IsActiveAt(IScheduleWindow window, DateTime localNow)
    {
        // Date bounds are inclusive and compared by calendar date: a schedule whose start and end
        // fall on the same day is active for that whole day.
        if (window.StartDate.HasValue && localNow.Date < window.StartDate.Value.Date) return false;
        if (window.EndDate.HasValue && localNow.Date > window.EndDate.Value.Date) return false;

        if (window.StartTime is not null || window.EndTime is not null)
        {
            if (!new DailyWindow(window.StartTime, window.EndTime).Contains(TimeOnly.FromDateTime(localNow)))
                return false;
        }

        return IsDayActive(window, localNow.DayOfWeek);
    }

    /// <summary>No day selected means every day.</summary>
    public static bool IsDayActive(IScheduleWindow window, DayOfWeek localDayOfWeek)
    {
        if (!window.Sunday && !window.Monday && !window.Tuesday && !window.Wednesday
            && !window.Thursday && !window.Friday && !window.Saturday)
            return true;

        return localDayOfWeek switch
        {
            DayOfWeek.Sunday => window.Sunday,
            DayOfWeek.Monday => window.Monday,
            DayOfWeek.Tuesday => window.Tuesday,
            DayOfWeek.Wednesday => window.Wednesday,
            DayOfWeek.Thursday => window.Thursday,
            DayOfWeek.Friday => window.Friday,
            DayOfWeek.Saturday => window.Saturday,
            _ => false
        };
    }
}
