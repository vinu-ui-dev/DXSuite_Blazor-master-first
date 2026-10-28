using System.Text.Json;
using DXSuite.Director.Entities;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services;

/// <summary>
/// Recurrence window stored in TblUserGroupLocationDefault.StateScheduleJson.
/// Times are "HH:mm"; null start/end mean start/end of day. Days holds
/// "Sun".."Sat"; null or empty means every day.
/// </summary>
public sealed class AirlineStateSchedule
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public List<string>? Days { get; set; }
}

/// <summary>
/// Picks the active content state for an airline at a wall. All consumers of
/// tblUserGroupLocationDefault must resolve through this so a second state row
/// never leaks into surfaces that expect one row per (group, location).
/// </summary>
public static class AirlineStateResolver
{
    private static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    /// <summary>
    /// Returns the state to play at <paramref name="at"/>: the first schedule-matching
    /// non-default state (by name, for determinism), else the default state, else the
    /// first row. Null when the sequence is empty.
    /// </summary>
    public static TblUserGroupLocationDefault? PickActive(
        IEnumerable<TblUserGroupLocationDefault> states, DateTime at, ILogger? logger = null)
    {
        var list = states as IList<TblUserGroupLocationDefault> ?? states.ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        var scheduled = list
            .Where(s => !s.IsDefaultState && IsScheduledActive(s, at, logger))
            .OrderBy(s => s.StateName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (scheduled != null) return scheduled;

        return list.FirstOrDefault(s => s.IsDefaultState)
            ?? list.OrderBy(s => s.StateName, StringComparer.OrdinalIgnoreCase).First();
    }

    /// <summary>
    /// A non-default state whose schedule is missing or unreadable is inactive. That was previously
    /// indistinguishable from an unscheduled state, so it is logged: the row is authored and
    /// something is wrong with it, rather than absent.
    /// </summary>
    private static bool IsScheduledActive(TblUserGroupLocationDefault state, DateTime at, ILogger? logger)
    {
        var schedule = Parse(state.StateScheduleJson);
        if (schedule == null)
        {
            logger?.LogWarning(
                "Airline state '{StateName}' (group {UserGroupId}, location {LocationId}) has a missing or "
                + "unreadable schedule and is treated as inactive.",
                state.StateName, state.UserGroupId, state.LocationId);
            return false;
        }

        return IsActiveAt(schedule, at);
    }

    public static bool IsActiveAt(string? scheduleJson, DateTime at)
    {
        var schedule = Parse(scheduleJson);
        return schedule != null && IsActiveAt(schedule, at);
    }

    public static bool IsActiveAt(AirlineStateSchedule schedule, DateTime at)
    {
        if (schedule.Days is { Count: > 0 }
            && !schedule.Days.Contains(DayNames[(int)at.DayOfWeek], StringComparer.OrdinalIgnoreCase))
            return false;

        return new DailyWindow(ParseTime(schedule.StartTime), ParseTime(schedule.EndTime))
            .Contains(TimeOnly.FromDateTime(at));
    }

    public static AirlineStateSchedule? Parse(string? scheduleJson)
    {
        if (string.IsNullOrWhiteSpace(scheduleJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AirlineStateSchedule>(scheduleJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static TimeOnly? ParseTime(string? value)
        => TimeOnly.TryParse(value, out var t) ? t : null;
}
