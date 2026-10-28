using System.Globalization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Reports;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// An Activation's times (MSG-16, MSG-17): worked out once at send by <see cref="Resolve"/>, and read per display by
/// <see cref="IsShowingAt"/>. Send now runs on instants. A scheduled send stores its date range and weekly window as
/// wall-clock bounds, which mean that reading on each display's own clock, so one send starts at a different instant
/// in each time zone it reaches (GLOSSARY "Wall-clock bound").
/// </summary>
public static class ActivationTiming
{
    /// <summary>
    /// The Activation's times for a send whose screens sit in <paramref name="screenTimeZones"/>. This is the one place an
    /// Activation's end is worked out: 1.2's repeat count (AM-13) caps it here, the earlier end winning.
    /// </summary>
    public static ResolvedTiming Resolve(MessageSendTiming when, DateTime nowUtc, IReadOnlyCollection<TimeZoneInfo> screenTimeZones)
    {
        switch (when)
        {
            case MessageSendTiming.SendNow sendNow:
                return new ResolvedTiming(nowUtc, sendNow.Minutes is { } minutes ? nowUtc.AddMinutes(minutes) : null, null, null, null);

            case MessageSendTiming.Scheduled scheduled:
                if (screenTimeZones.Count == 0) throw new ArgumentException("A scheduled send needs the time zone of at least one screen.", nameof(screenTimeZones));
                // The instants the range spans across every screen, so UTC queries see the whole Activation.
                var startUtc = screenTimeZones.Min(timeZone => ToUtc(scheduled.Start, timeZone));
                var endUtc = screenTimeZones.Max(timeZone => ToUtc(scheduled.End, timeZone));
                return new ResolvedTiming(startUtc, endUtc, WallClock(scheduled.Start), WallClock(scheduled.End), scheduled.Weekly);

            default:
                throw new NotSupportedException($"Unknown send timing {when.GetType().Name}.");
        }
    }

    /// <summary>
    /// Whether the Activation's timing puts it on a display in <paramref name="timeZone"/> at <paramref name="now"/>: not
    /// cancelled, inside its range, on one of its days and inside its daily window (MSG-17). Whether a canned Message
    /// is still Approved is the caller's check.
    /// </summary>
    public static bool IsShowingAt(TblMessageActivation activation, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (activation.CancelledUtc is not null) return false;

        var local = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        if (activation is { StartLocal: { } start, EndLocal: { } end })
        {
            if (local < start || local >= end) return false;
        }
        else
        {
            var utc = now.UtcDateTime;
            if (utc < activation.StartUtc || (activation.EndUtc is { } endUtc && utc >= endUtc)) return false;
        }

        var time = TimeOnly.FromDateTime(local);
        if (!new DailyWindow(activation.WindowStartTime, activation.WindowEndTime).Contains(time)) return false;

        // The small hours of an overnight window belong to the night it started, so the day before decides.
        var overnightTail = activation.WindowStartTime > activation.WindowEndTime && time < activation.WindowEndTime;
        return ShowsOn(activation, overnightTail ? local.AddDays(-1).DayOfWeek : local.DayOfWeek);
    }

    /// <summary>
    /// The end the delivery route publishes for an Activation showing on a display in <paramref name="timeZone"/> at
    /// <paramref name="now"/> (INK-08): Send now's own end (null until cancelled); for a scheduled send, the end of its
    /// current occurrence, which is today's daily window or its date range, whichever ends first. With days but no daily
    /// window, an occurrence runs to the start of the first day it does not show on.
    /// </summary>
    public static DateTime? CurrentOccurrenceEndUtc(TblMessageActivation activation, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (activation is not { StartLocal: not null, EndLocal: { } end }) return activation.EndUtc;

        var local = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        DateTime occurrenceEnd;
        if (activation is { WindowStartTime: { } from, WindowEndTime: { } until })
        {
            // An overnight window that opened this evening closes tomorrow morning.
            var overnightTonight = from > until && TimeOnly.FromDateTime(local) >= from;
            occurrenceEnd = local.Date.AddDays(overnightTonight ? 1 : 0) + until.ToTimeSpan();
        }
        else if (Enum.GetValues<DayOfWeek>().All(day => ShowsOn(activation, day)))
        {
            occurrenceEnd = end;
        }
        else
        {
            occurrenceEnd = local.Date.AddDays(1);
            while (ShowsOn(activation, occurrenceEnd.DayOfWeek)) occurrenceEnd = occurrenceEnd.AddDays(1);
        }

        return ToUtc(occurrenceEnd < end ? occurrenceEnd : end, timeZone);
    }

    /// <summary>
    /// The instant a wall-clock time names in <paramref name="timeZone"/>. A time skipped when clocks spring forward
    /// resolves to the first instant after the gap; a time repeated when they fall back resolves to its first
    /// occurrence (GLOSSARY "Daylight saving transitions").
    /// </summary>
    public static DateTime ToUtc(DateTime wallClock, TimeZoneInfo timeZone)
    {
        var local = WallClock(wallClock);
        if (timeZone.IsInvalidTime(local))
        {
            // Every time zone's gap starts and ends on a whole minute, so the first valid minute is the gap's end.
            local = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute));
            while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // The first occurrence is the one under the larger offset (daylight time, before the clocks go back).
            var offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    /// <summary>
    /// The weekly window as a list shows it: its days, runs of days joined ("Mon–Fri", "Mon, Wed"), then its daily
    /// window ("09:00–17:00"). Null when it shows every day, all day.
    /// </summary>
    public static string? DescribeWindow(TblMessageActivation activation)
    {
        // Monday first, as the schedule editor lists the days.
        DayOfWeek[] week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
            DayOfWeek.Saturday, DayOfWeek.Sunday];
        var shows = week.Select(day => ShowsOn(activation, day)).ToArray();
        var hours = activation is { WindowStartTime: { } from, WindowEndTime: { } until }
            ? $"{from.ToString("HH:mm", CultureInfo.InvariantCulture)}–{until.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : null;
        if (shows.All(s => s)) return hours is null ? null : $"Every day {hours}";

        var runs = new List<string>();
        for (var i = 0; i < week.Length; i++)
        {
            if (!shows[i]) continue;
            var last = i;
            while (last + 1 < week.Length && shows[last + 1]) last++;
            runs.Add(last - i >= 2 ? $"{Short(week[i])}–{Short(week[last])}" : string.Join(", ", Enumerable.Range(i, last - i + 1).Select(d => Short(week[d]))));
            i = last;
        }
        var days = runs.Count == 0 ? "No days" : string.Join(", ", runs);
        return hours is null ? days : $"{days} {hours}";
    }

    private static string Short(DayOfWeek day) => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(day);

    private static DateTime WallClock(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static bool ShowsOn(TblMessageActivation activation, DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => activation.Sunday,
        DayOfWeek.Monday => activation.Monday,
        DayOfWeek.Tuesday => activation.Tuesday,
        DayOfWeek.Wednesday => activation.Wednesday,
        DayOfWeek.Thursday => activation.Thursday,
        DayOfWeek.Friday => activation.Friday,
        _ => activation.Saturday,
    };
}

/// <summary>
/// An Activation's times as a send stores them. <see cref="StartLocal"/> and <see cref="EndLocal"/> are set only for a
/// scheduled send; a null <see cref="Weekly"/> is every day, all day.
/// </summary>
public sealed record ResolvedTiming(DateTime StartUtc, DateTime? EndUtc, DateTime? StartLocal, DateTime? EndLocal, MessageWeeklyWindow? Weekly)
{
    public void ApplyTo(TblMessageActivation activation)
    {
        activation.StartUtc = StartUtc;
        activation.EndUtc = EndUtc;
        activation.StartLocal = StartLocal;
        activation.EndLocal = EndLocal;
        activation.Sunday = Shows(DayOfWeek.Sunday);
        activation.Monday = Shows(DayOfWeek.Monday);
        activation.Tuesday = Shows(DayOfWeek.Tuesday);
        activation.Wednesday = Shows(DayOfWeek.Wednesday);
        activation.Thursday = Shows(DayOfWeek.Thursday);
        activation.Friday = Shows(DayOfWeek.Friday);
        activation.Saturday = Shows(DayOfWeek.Saturday);
        activation.WindowStartTime = Weekly?.DailyStart;
        activation.WindowEndTime = Weekly?.DailyEnd;
    }

    private bool Shows(DayOfWeek day) => Weekly is null || Weekly.Days.Contains(day);
}

/// <summary>
/// When an Activation shows, as the lists show it (MSG-16): a send now by its instants, read in the viewer's time zone;
/// a scheduled send by its wall-clock range and weekly window, which every screen reads on its own clock, so the
/// UTC envelope (<see cref="StartUtc"/>, <see cref="EndUtc"/>) is not what the sender chose.
/// </summary>
/// <param name="Window">The weekly window, such as "Mon–Fri 09:00–17:00"; null when it shows every day, all day.</param>
public sealed record ActivationWhen(DateTime StartUtc, DateTime? EndUtc, DateTime? StartLocal, DateTime? EndLocal, string? Window)
{
    /// <summary>Tells the viewer that a scheduled send's times are each screen's own, not the viewer's.</summary>
    public const string ScreenTimeNote = "screen time";

    private const string Format = "yyyy-MM-dd HH:mm";

    public static ActivationWhen Of(TblMessageActivation activation) => new(
        activation.StartUtc, activation.EndUtc, activation.StartLocal, activation.EndLocal, ActivationTiming.DescribeWindow(activation));

    public bool IsScheduled => StartLocal is not null;

    public string StartText(ReportTimeZone viewer) =>
        (StartLocal ?? viewer.ToLocal(StartUtc)).ToString(Format, CultureInfo.InvariantCulture);

    public string EndText(ReportTimeZone viewer) =>
        EndLocal is { } endLocal ? endLocal.ToString(Format, CultureInfo.InvariantCulture)
        : EndUtc is { } endUtc ? viewer.ToLocal(endUtc).ToString(Format, CultureInfo.InvariantCulture)
        : "Until cancelled";
}
