using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Validates access schedules for conflicts. Two schedules overlap when their
/// date ranges, time windows, and days-of-week all intersect.
/// </summary>
public static class AccessScheduleValidator
{
    /// <summary>
    /// Checks whether a proposed schedule overlaps with any existing schedules
    /// on the same access rule. Returns error messages for each conflict found.
    /// </summary>
    public static List<string> ValidateNoOverlap(
        IEnumerable<TblAccessSchedule> existing,
        TblAccessSchedule proposed,
        int? excludeScheduleId = null)
    {
        var errors = new List<string>();

        foreach (var other in existing)
        {
            if (excludeScheduleId.HasValue && other.AccessScheduleId == excludeScheduleId.Value)
                continue;

            // Only compare schedules targeting the same display scope
            if (proposed.DisplayId != other.DisplayId)
                continue;

            if (DateRangesOverlap(proposed, other) &&
                TimeWindowsOverlap(proposed, other) &&
                DaysOverlap(proposed, other))
            {
                errors.Add($"Schedule conflicts with existing schedule #{other.AccessScheduleId} " +
                           $"({FormatTimeRange(other)}).");
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates that time fields are paired correctly.
    /// </summary>
    public static List<string> ValidateFields(TblAccessSchedule schedule)
    {
        var errors = new List<string>();

        if (schedule.StartTime.HasValue != schedule.EndTime.HasValue)
            errors.Add("Start time and end time must both be set or both be empty.");

        if (schedule.StartTime.HasValue && schedule.EndTime.HasValue &&
            schedule.StartTime.Value == schedule.EndTime.Value)
            errors.Add("Start time and end time cannot be the same.");

        return errors;
    }

    private static bool DateRangesOverlap(IScheduleWindow a, IScheduleWindow b)
    {
        // Unbounded ranges always overlap
        var aStart = a.StartDate ?? DateTime.MinValue;
        var aEnd = a.EndDate ?? DateTime.MaxValue;
        var bStart = b.StartDate ?? DateTime.MinValue;
        var bEnd = b.EndDate ?? DateTime.MaxValue;

        return aStart.Date <= bEnd.Date && bStart.Date <= aEnd.Date;
    }

    private static bool TimeWindowsOverlap(IScheduleWindow a, IScheduleWindow b)
    {
        // No time window on either = always active = overlap
        if (!a.StartTime.HasValue || !a.EndTime.HasValue ||
            !b.StartTime.HasValue || !b.EndTime.HasValue)
            return true;

        return TimesOverlap(
            a.StartTime.Value, a.EndTime.Value,
            b.StartTime.Value, b.EndTime.Value);
    }

    private static bool TimesOverlap(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd)
    {
        bool aNormal = aStart <= aEnd;
        bool bNormal = bStart <= bEnd;

        if (aNormal && bNormal)
        {
            // Both normal windows
            return aStart < bEnd && bStart < aEnd;
        }

        if (!aNormal && !bNormal)
        {
            // Both overnight — always overlap (both cover midnight)
            return true;
        }

        // One normal, one overnight
        var (normalStart, normalEnd) = aNormal ? (aStart, aEnd) : (bStart, bEnd);
        var (overnightStart, overnightEnd) = aNormal ? (bStart, bEnd) : (aStart, aEnd);

        // Overnight window is equivalent to [start, 24:00) + [00:00, end)
        return normalStart < overnightEnd || normalEnd > overnightStart;
    }

    private static bool DaysOverlap(IScheduleWindow a, IScheduleWindow b)
    {
        bool aAnyDay = a.Sunday || a.Monday || a.Tuesday || a.Wednesday || a.Thursday || a.Friday || a.Saturday;
        bool bAnyDay = b.Sunday || b.Monday || b.Tuesday || b.Wednesday || b.Thursday || b.Friday || b.Saturday;

        // No days set = every day
        if (!aAnyDay || !bAnyDay)
            return true;

        return (a.Sunday && b.Sunday) || (a.Monday && b.Monday) || (a.Tuesday && b.Tuesday) ||
               (a.Wednesday && b.Wednesday) || (a.Thursday && b.Thursday) ||
               (a.Friday && b.Friday) || (a.Saturday && b.Saturday);
    }

    private static string FormatTimeRange(TblAccessSchedule schedule)
    {
        var parts = new List<string>();

        if (schedule.StartTime.HasValue && schedule.EndTime.HasValue)
            parts.Add($"{schedule.StartTime.Value:HH\\:mm}-{schedule.EndTime.Value:HH\\:mm}");

        if (schedule.StartDate.HasValue || schedule.EndDate.HasValue)
        {
            var dateRange = $"{schedule.StartDate?.ToString("MM/dd") ?? "..."}-{schedule.EndDate?.ToString("MM/dd") ?? "..."}";
            parts.Add(dateRange);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Always";
    }
}
