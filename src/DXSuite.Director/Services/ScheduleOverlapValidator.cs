using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Stateless overlap checker for TblScheduleEvent records.
/// Mirrors AccessScheduleValidator: a conflict requires all three of
/// date-range overlap ∧ time-window overlap ∧ day-of-week overlap.
/// </summary>
public static class ScheduleOverlapValidator
{
    /// <summary>
    /// Returns a description string for every event in <paramref name="existingForDisplay"/>
    /// that overlaps the proposed time/date/day window.
    /// </summary>
    /// <param name="existingForDisplay">Events already saved for the same display.</param>
    /// <param name="newStart">Proposed start time (minute-precise).</param>
    /// <param name="newEnd">Proposed end time (exclusive upper bound).</param>
    /// <param name="newStartDate">Optional date-range start; null = unbounded.</param>
    /// <param name="newEndDate">Optional date-range end; null = unbounded.</param>
    /// <param name="daysActive">7-element array [sun,mon,tue,wed,thu,fri,sat]; all-false treated as every day.</param>
    /// <param name="excludeEventId">ID of the event being edited — excluded from conflict search.</param>
    /// <param name="groupNameResolver">Maps UserGroupId → display name for conflict descriptions.</param>
    public static List<string> FindOverlaps(
        IEnumerable<TblScheduleEvent> existingForDisplay,
        TimeOnly newStart,
        TimeOnly newEnd,
        DateTime? newStartDate,
        DateTime? newEndDate,
        bool[] daysActive,
        int? excludeEventId,
        Func<int?, string> groupNameResolver)
    {
        var conflicts = new List<string>();
        bool newAnyDay = !daysActive.Any(d => d);

        foreach (var ev in existingForDisplay)
        {
            if (excludeEventId.HasValue && ev.ScheduleEventId == excludeEventId.Value) continue;
            if (ev.UserGroupId == null) continue;

            // 1. Date ranges must overlap
            if (!DateRangesOverlap(ev.StartDate, ev.EndDate, newStartDate, newEndDate)) continue;

            // 2. Time windows must overlap
            var evStart = ev.StartTime ?? TimeOnly.MinValue;
            var evEnd   = ev.EndTime   ?? new TimeOnly(23, 59);
            if (!TimeWindowsOverlap(evStart, evEnd, newStart, newEnd)) continue;

            // 3. At least one shared active day
            bool evAnyDay = !(ev.Sunday || ev.Monday || ev.Tuesday || ev.Wednesday ||
                              ev.Thursday || ev.Friday || ev.Saturday);
            if (!newAnyDay && !evAnyDay && !DaysOverlap(ev, daysActive)) continue;

            var name = groupNameResolver(ev.UserGroupId);
            var timeLabel = $"{evStart:HH\\:mm}–{evEnd:HH\\:mm}";
            conflicts.Add($"{name} ({timeLabel})");
        }

        return conflicts;
    }

    /// <summary>
    /// Overlap check for display schedule events (no counter-group filter).
    /// Null times mean all-day; StartTime &gt; EndTime is an overnight window,
    /// which the runtime evaluates as two same-day segments (00:00–end and
    /// start–24:00) for each active day — mirrored here.
    ///
    /// Conflicts are only reported when the two rules can be active on the same
    /// future calendar date: the shared date window is intersected with
    /// [today, ∞), so fully-elapsed entries never block new ones, and a short
    /// shared window is checked against the actual weekdays it contains rather
    /// than treating empty day flags as "every day".
    /// </summary>
    /// <param name="existingForDisplay">Non-default events already saved for the same display.</param>
    /// <param name="proposed">The event being created or edited.</param>
    public static List<string> FindDisplayOverlaps(
        IEnumerable<TblScheduleEvent> existingForDisplay,
        TblScheduleEvent proposed,
        DateTime? today = null)
    {
        var conflicts = new List<string>();
        var floor = (today ?? DateTime.Today).Date;

        foreach (var ev in existingForDisplay)
        {
            if (ev.DefaultContent) continue;
            if (proposed.ScheduleEventId != 0 && ev.ScheduleEventId == proposed.ScheduleEventId) continue;

            // An existing entry whose window has fully elapsed can never play
            // again, so it cannot be a conflict source for anything new.
            if (ev.EndDate != null && ev.EndDate.Value.Date < floor) continue;

            // Shared date window across both rules — deliberately NOT clamped to
            // today: the calendar renders the whole current week, so a proposed
            // entry dated earlier this week must still validate against live rules.
            var windowStart = DateTime.MinValue;
            if (ev.StartDate != null && ev.StartDate.Value.Date > windowStart) windowStart = ev.StartDate.Value.Date;
            if (proposed.StartDate != null && proposed.StartDate.Value.Date > windowStart) windowStart = proposed.StartDate.Value.Date;

            DateTime? windowEnd = null;
            if (ev.EndDate != null) windowEnd = ev.EndDate.Value.Date;
            if (proposed.EndDate != null && (windowEnd == null || proposed.EndDate.Value.Date < windowEnd))
                windowEnd = proposed.EndDate.Value.Date;

            if (windowEnd != null && windowEnd < windowStart) continue;

            if (!SharedActiveDayExists(ev, proposed, windowStart, windowEnd)) continue;
            if (!WrapAwareTimeWindowsOverlap(ev.StartTime, ev.EndTime, proposed.StartTime, proposed.EndTime)) continue;

            conflicts.Add(DescribeEvent(ev));
        }

        return conflicts;
    }

    // True when at least one date in the shared window is an active weekday for
    // both rules. A window of 7+ days contains every weekday, so the flag sets
    // are compared directly; a shorter window is enumerated date by date.
    private static bool SharedActiveDayExists(TblScheduleEvent a, TblScheduleEvent b, DateTime windowStart, DateTime? windowEnd)
    {
        if (windowEnd == null || (windowEnd.Value - windowStart).TotalDays >= 6)
            return DaySetsOverlap(a, b);

        for (var date = windowStart; date <= windowEnd.Value; date = date.AddDays(1))
        {
            if (ActiveOnDay(a, date.DayOfWeek) && ActiveOnDay(b, date.DayOfWeek)) return true;
        }
        return false;
    }

    private static bool ActiveOnDay(TblScheduleEvent ev, DayOfWeek day)
    {
        bool anyDay = !(ev.Sunday || ev.Monday || ev.Tuesday || ev.Wednesday || ev.Thursday || ev.Friday || ev.Saturday);
        if (anyDay) return true;

        return day switch
        {
            DayOfWeek.Sunday    => ev.Sunday,
            DayOfWeek.Monday    => ev.Monday,
            DayOfWeek.Tuesday   => ev.Tuesday,
            DayOfWeek.Wednesday => ev.Wednesday,
            DayOfWeek.Thursday  => ev.Thursday,
            DayOfWeek.Friday    => ev.Friday,
            DayOfWeek.Saturday  => ev.Saturday,
            _                   => false
        };
    }

    private static string DescribeEvent(TblScheduleEvent ev)
    {
        var time = ev.StartTime == null && ev.EndTime == null
            ? "all day"
            : $"{(ev.StartTime ?? TimeOnly.MinValue):h\\:mm tt}–{(ev.EndTime ?? new TimeOnly(23, 59)):h\\:mm tt}";

        var days = new List<string>();
        if (ev.Sunday) days.Add("Sun");
        if (ev.Monday) days.Add("Mon");
        if (ev.Tuesday) days.Add("Tue");
        if (ev.Wednesday) days.Add("Wed");
        if (ev.Thursday) days.Add("Thu");
        if (ev.Friday) days.Add("Fri");
        if (ev.Saturday) days.Add("Sat");
        var dayLabel = days.Count is 0 or 7 ? "every day" : string.Join("/", days);

        var dates = ev.StartDate == null && ev.EndDate == null
            ? ""
            : $" ({ev.StartDate?.ToString("MMM d") ?? "…"}–{ev.EndDate?.ToString("MMM d") ?? "…"})";

        return $"Schedule entry {dayLabel} {time}{dates}";
    }

    private static bool DaySetsOverlap(TblScheduleEvent a, TblScheduleEvent b)
    {
        bool aAny = !(a.Sunday || a.Monday || a.Tuesday || a.Wednesday || a.Thursday || a.Friday || a.Saturday);
        bool bAny = !(b.Sunday || b.Monday || b.Tuesday || b.Wednesday || b.Thursday || b.Friday || b.Saturday);
        if (aAny || bAny) return true;

        return (a.Sunday && b.Sunday) || (a.Monday && b.Monday) || (a.Tuesday && b.Tuesday)
            || (a.Wednesday && b.Wednesday) || (a.Thursday && b.Thursday)
            || (a.Friday && b.Friday) || (a.Saturday && b.Saturday);
    }

    private static bool WrapAwareTimeWindowsOverlap(TimeOnly? aStart, TimeOnly? aEnd, TimeOnly? bStart, TimeOnly? bEnd)
    {
        foreach (var (as1, ae1) in DaySegments(aStart, aEnd))
        {
            foreach (var (bs1, be1) in DaySegments(bStart, bEnd))
            {
                if (bs1 < ae1 && be1 > as1) return true;
            }
        }
        return false;
    }

    // Minutes-from-midnight segments a window occupies within a single day
    private static IEnumerable<(int Start, int End)> DaySegments(TimeOnly? start, TimeOnly? end)
    {
        const int endOfDay = 24 * 60;
        var s = start != null ? (int)start.Value.ToTimeSpan().TotalMinutes : 0;
        var e = end != null ? (int)end.Value.ToTimeSpan().TotalMinutes : endOfDay;

        if (start != null && end != null && s > e)
        {
            yield return (0, e);
            yield return (s, endOfDay);
        }
        else
        {
            yield return (s, e);
        }
    }

    private static bool DateRangesOverlap(
        DateTime? aStart, DateTime? aEnd,
        DateTime? bStart, DateTime? bEnd)
    {
        // A ends before B starts → no overlap
        if (aEnd.HasValue && bStart.HasValue && aEnd.Value < bStart.Value) return false;
        // B ends before A starts → no overlap
        if (bEnd.HasValue && aStart.HasValue && bEnd.Value < aStart.Value) return false;
        return true;
    }

    private static bool TimeWindowsOverlap(
        TimeOnly aStart, TimeOnly aEnd,
        TimeOnly bStart, TimeOnly bEnd)
    {
        // Both ranges are assumed to not wrap midnight (TicketCounterScheduler enforces end > start).
        return bStart < aEnd && bEnd > aStart;
    }

    private static bool DaysOverlap(TblScheduleEvent ev, bool[] days)
    {
        // days[0]=Sun, [1]=Mon, ... [6]=Sat
        return (days[0] && ev.Sunday)
            || (days[1] && ev.Monday)
            || (days[2] && ev.Tuesday)
            || (days[3] && ev.Wednesday)
            || (days[4] && ev.Thursday)
            || (days[5] && ev.Friday)
            || (days[6] && ev.Saturday);
    }
}
