namespace DXSuite.Director.Services;

/// <summary>
/// Shared time-of-day window check for schedule events. Retained for its nine call sites;
/// <see cref="DailyWindow"/> holds the rule and this is a thin adapter onto it.
/// </summary>
public static class ScheduleTime
{
    public static bool IsWithinWindow(TimeOnly time, TimeOnly? start, TimeOnly? end)
        => new DailyWindow(start, end).Contains(time);
}
