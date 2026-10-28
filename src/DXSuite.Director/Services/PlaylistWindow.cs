using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services;

/// <summary>
/// Whether one playlist asset is active at a device's local wall clock, over the four window fields
/// its sidecar carries.
///
/// This evaluation is new on the server. The fields were parsed and forwarded to the player
/// unevaluated, which is how day-of-week came to be decided on the server's clock while time-of-day
/// was decided on the device's: a window crossing midnight in a non-server zone got the wrong day
/// with the right time.
/// </summary>
public static class PlaylistWindow
{
    public static bool IsActiveAt(PlayerContentItem asset, DateTime localNow)
    {
        if (!IsDayActive(asset.Days, localNow.DayOfWeek))
            return false;

        if (asset.StartDate.HasValue && localNow.Date < asset.StartDate.Value.Date) return false;
        if (asset.EndDate.HasValue && localNow.Date > asset.EndDate.Value.Date) return false;

        if (asset.StartTime is null && asset.EndTime is null)
            return true;

        return new DailyWindow(ToTimeOfDay(asset.StartTime), ToTimeOfDay(asset.EndTime))
            .Contains(TimeOnly.FromDateTime(localNow));
    }

    /// <summary>Null or empty Days means every day.</summary>
    public static bool IsDayActive(string? days, DayOfWeek localDayOfWeek)
    {
        if (string.IsNullOrWhiteSpace(days)) return true;

        var todayAbbrev = localDayOfWeek switch
        {
            DayOfWeek.Monday => "Mo",
            DayOfWeek.Tuesday => "Tu",
            DayOfWeek.Wednesday => "We",
            DayOfWeek.Thursday => "Th",
            DayOfWeek.Friday => "Fr",
            DayOfWeek.Saturday => "Sa",
            DayOfWeek.Sunday => "Su",
            _ => ""
        };

        return days.Split(',', StringSplitOptions.TrimEntries).Contains(todayAbbrev, StringComparer.OrdinalIgnoreCase);
    }

    // A sidecar is customer-authored, so a value outside a day is possible. It reads as no bound
    // rather than failing the whole manifest for one malformed file.
    private static TimeOnly? ToTimeOfDay(TimeSpan? value)
        => value is { } v && v >= TimeSpan.Zero && v < TimeSpan.FromDays(1)
            ? TimeOnly.FromTimeSpan(v)
            : null;
}
