using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using Xunit;

namespace DXSuite.Director.Tests;

// Clause and assumption ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// When an Activation shows (MSG-16, MSG-17): Send now runs on instants; a scheduled send's date range and weekly
/// window are wall-clock times read in each display's own time zone. No database.
/// </summary>
public class ActivationTimingTests
{
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static readonly TimeZoneInfo Vancouver = TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver");

    [Fact]
    public void A_scheduled_date_range_is_read_in_each_displays_own_time_zone()
    {
        // 21 Oct 2026, 09:00 to 17:00 on each display's wall clock. Toronto is UTC-4 then, Vancouver UTC-7.
        var activation = Scheduled(new(2026, 10, 21, 9, 0, 0), new(2026, 10, 21, 17, 0, 0));

        // 13:30 UTC is 09:30 in Toronto and 06:30 in Vancouver.
        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 13, 30), Toronto));
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 13, 30), Vancouver));
        // 21:00 UTC is 17:00 in Toronto (ended: the end is exclusive); 21:30 UTC is 14:30 in Vancouver.
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 21, 0), Toronto));
        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 21, 30), Vancouver));
        // 00:00 UTC on the 22nd is 17:00 in Vancouver: ended there too.
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 22, 0, 0), Vancouver));
    }

    [Fact]
    public void A_weekly_window_shows_only_on_its_days_inside_the_daily_window()
    {
        // Two weeks, weekdays only, 08:00 to 18:00 Toronto time. 23 Oct 2026 is a Friday.
        var activation = Scheduled(new(2026, 10, 19, 0, 0, 0), new(2026, 11, 2, 0, 0, 0),
            days: [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            from: new TimeOnly(8, 0), until: new TimeOnly(18, 0));

        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 23, 11, 59), Toronto)); // Fri 07:59
        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 23, 12, 0), Toronto));   // Fri 08:00
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 23, 22, 0), Toronto));  // Fri 18:00
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 24, 14, 0), Toronto));  // Sat 10:00
        // Fri 18:30 in Toronto is 15:30 in Vancouver: still inside the window there.
        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 23, 22, 30), Vancouver));
    }

    [Fact]
    public void An_overnight_daily_window_belongs_to_the_day_it_starts_on()
    {
        // Mondays only, 22:00 to 02:00. 19 Oct 2026 is a Monday.
        var activation = Scheduled(new(2026, 10, 1, 0, 0, 0), new(2026, 11, 1, 0, 0, 0),
            days: [DayOfWeek.Monday], from: new TimeOnly(22, 0), until: new TimeOnly(2, 0));

        Assert.True(ActivationTiming.IsShowingAt(activation, Local(2026, 10, 19, 23, 0, Toronto), Toronto));  // Mon 23:00
        Assert.True(ActivationTiming.IsShowingAt(activation, Local(2026, 10, 20, 1, 0, Toronto), Toronto));   // Tue 01:00, Monday's night
        Assert.False(ActivationTiming.IsShowingAt(activation, Local(2026, 10, 19, 1, 0, Toronto), Toronto));  // Mon 01:00, Sunday's night
        Assert.False(ActivationTiming.IsShowingAt(activation, Local(2026, 10, 20, 22, 30, Toronto), Toronto)); // Tue 22:30
    }

    [Fact]
    public void Send_now_runs_on_instants_whatever_the_zone_and_a_cancelled_Activation_shows_nowhere()
    {
        var activation = new TblMessageActivation { StartUtc = new(2026, 10, 21, 13, 0, 0), EndUtc = new(2026, 10, 21, 13, 30, 0) };

        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 13, 0), Toronto));
        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 13, 0), Vancouver));
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 13, 30), Vancouver));
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 12, 59), Toronto));

        activation.EndUtc = null;
        Assert.True(ActivationTiming.IsShowingAt(activation, Utc(2027, 1, 1, 0, 0), Toronto));
        activation.CancelledUtc = new(2026, 10, 21, 13, 10, 0);
        Assert.False(ActivationTiming.IsShowingAt(activation, Utc(2026, 10, 21, 13, 20), Toronto));
    }

    [Fact]
    public void A_scheduled_send_stores_its_wall_clock_range_and_the_instants_it_spans_across_its_screens()
    {
        var when = new MessageSendTiming.Scheduled(new(2026, 10, 21, 9, 0, 0), new(2026, 10, 21, 17, 0, 0));

        var resolved = ActivationTiming.Resolve(when, new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc), [Toronto, Vancouver]);

        Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0), resolved.StartLocal);
        Assert.Equal(new DateTime(2026, 10, 21, 17, 0, 0), resolved.EndLocal);
        // Toronto starts first (13:00 UTC); Vancouver ends last (00:00 UTC next day).
        Assert.Equal(new DateTime(2026, 10, 21, 13, 0, 0), resolved.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 22, 0, 0, 0), resolved.EndUtc);
    }

    [Fact]
    public void Send_now_starts_at_the_send_and_ends_after_its_minutes_or_never()
    {
        var now = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

        var forMinutes = ActivationTiming.Resolve(new MessageSendTiming.SendNow(15), now, [Toronto]);
        var untilCancelled = ActivationTiming.Resolve(new MessageSendTiming.SendNow(null), now, [Toronto]);

        Assert.Equal((now, now.AddMinutes(15)), (forMinutes.StartUtc, forMinutes.EndUtc));
        Assert.Null(forMinutes.StartLocal);
        Assert.Null(untilCancelled.EndUtc);
    }

    [Fact]
    public void Wall_clock_times_lost_or_repeated_by_daylight_saving_resolve_to_the_first_instant()
    {
        // Toronto springs forward at 02:00 on 8 Mar 2026: 02:30 does not exist and resolves to 03:00 EDT (07:00 UTC).
        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0), ActivationTiming.ToUtc(new(2026, 3, 8, 2, 30, 0), Toronto));
        // Toronto falls back at 02:00 on 1 Nov 2026: 01:30 happens twice and resolves to the first, EDT (05:30 UTC).
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0), ActivationTiming.ToUtc(new(2026, 11, 1, 1, 30, 0), Toronto));
    }

    [Fact]
    public void A_weekly_window_is_described_by_its_runs_of_days_and_its_hours()
    {
        DayOfWeek[] weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
        var start = new DateTime(2026, 10, 19, 0, 0, 0);
        var end = new DateTime(2026, 11, 2, 0, 0, 0);

        Assert.Equal("Mon–Fri 09:00–17:00", ActivationTiming.DescribeWindow(Scheduled(start, end, weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0))));
        Assert.Equal("Mon, Wed, Sat, Sun", ActivationTiming.DescribeWindow(Scheduled(start, end,
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday])));
        Assert.Equal("Every day 22:00–02:00", ActivationTiming.DescribeWindow(Scheduled(start, end, Enum.GetValues<DayOfWeek>(),
            new TimeOnly(22, 0), new TimeOnly(2, 0))));
        Assert.Null(ActivationTiming.DescribeWindow(Scheduled(start, end)));
    }

    // INK-08: Send now publishes its own end (start plus N, or none); a scheduled send publishes the end of its current
    // occurrence on the display's clock, today's window or the date range, whichever ends first.
    [Fact]
    public void The_published_end_is_the_end_of_the_current_occurrence_on_the_displays_clock()
    {
        var sendNow = new TblMessageActivation { StartUtc = new(2026, 10, 21, 13, 0, 0), EndUtc = new(2026, 10, 21, 13, 30, 0) };
        Assert.Equal(new DateTime(2026, 10, 21, 13, 30, 0), ActivationTiming.CurrentOccurrenceEndUtc(sendNow, Utc(2026, 10, 21, 13, 5), Toronto));
        sendNow.EndUtc = null;
        Assert.Null(ActivationTiming.CurrentOccurrenceEndUtc(sendNow, Utc(2026, 10, 21, 13, 5), Toronto));

        // Weekdays 08:00 to 18:00 until 2 Nov: on Friday 23 Oct the occurrence ends at 18:00 that day, in each zone.
        DayOfWeek[] weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
        var windowed = Scheduled(new(2026, 10, 19, 0, 0, 0), new(2026, 11, 2, 0, 0, 0), weekdays, new TimeOnly(8, 0), new TimeOnly(18, 0));
        Assert.Equal(new DateTime(2026, 10, 23, 22, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(windowed, Utc(2026, 10, 23, 14, 0), Toronto));
        Assert.Equal(new DateTime(2026, 10, 24, 1, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(windowed, Utc(2026, 10, 23, 22, 30), Vancouver));

        // The range ends before today's window does: the range's end wins.
        var endsAtNoon = Scheduled(new(2026, 10, 19, 0, 0, 0), new(2026, 10, 23, 12, 0, 0), weekdays, new TimeOnly(8, 0), new TimeOnly(18, 0));
        Assert.Equal(new DateTime(2026, 10, 23, 16, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(endsAtNoon, Utc(2026, 10, 23, 14, 0), Toronto));

        // An overnight window ends the next morning when it started tonight, and this morning in its small hours.
        var overnight = Scheduled(new(2026, 10, 1, 0, 0, 0), new(2026, 11, 1, 0, 0, 0), [DayOfWeek.Monday], new TimeOnly(22, 0), new TimeOnly(2, 0));
        Assert.Equal(new DateTime(2026, 10, 20, 6, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(overnight, Local(2026, 10, 19, 23, 0, Toronto), Toronto));
        Assert.Equal(new DateTime(2026, 10, 20, 6, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(overnight, Local(2026, 10, 20, 1, 0, Toronto), Toronto));

        // Days with no daily window: the occurrence runs to the start of the first day it does not show on (Saturday).
        var weekdaysAllDay = Scheduled(new(2026, 10, 19, 0, 0, 0), new(2026, 11, 2, 0, 0, 0), weekdays);
        Assert.Equal(new DateTime(2026, 10, 24, 4, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(weekdaysAllDay, Local(2026, 10, 21, 10, 0, Toronto), Toronto));

        // Every day, no window: the date range's end.
        var range = Scheduled(new(2026, 10, 21, 9, 0, 0), new(2026, 10, 25, 17, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 25, 21, 0, 0), ActivationTiming.CurrentOccurrenceEndUtc(range, Local(2026, 10, 21, 10, 0, Toronto), Toronto));
    }

    private static TblMessageActivation Scheduled(DateTime start, DateTime end, DayOfWeek[]? days = null,
        TimeOnly? from = null, TimeOnly? until = null)
    {
        var resolved = ActivationTiming.Resolve(
            new MessageSendTiming.Scheduled(start, end, days is null ? null : new MessageWeeklyWindow(days.ToHashSet(), from, until)),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), [Toronto, Vancouver]);
        var activation = new TblMessageActivation();
        resolved.ApplyTo(activation);
        return activation;
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute, TimeZoneInfo zone)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
