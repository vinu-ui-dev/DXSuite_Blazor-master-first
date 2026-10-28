using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The one definition of an Activation that is active or scheduled (MSG-14, ZONE-12, A-33), and the status the Active
/// Messages tab shows for every Activation (MSG-18).
/// </summary>
public sealed class ActivationStateTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    public static TheoryData<DateTime, DateTime?, DateTime?, ActivationStatus> Cases => new()
    {
        { Now.AddHours(-1), null, null, ActivationStatus.Active },
        { Now.AddHours(-1), Now.AddMinutes(1), null, ActivationStatus.Active },
        { Now, Now.AddMinutes(1), null, ActivationStatus.Active },
        { Now.AddHours(1), null, null, ActivationStatus.Scheduled },
        { Now.AddHours(1), Now.AddHours(2), null, ActivationStatus.Scheduled },
        { Now.AddHours(-2), Now.AddHours(-1), null, ActivationStatus.Ended },
        { Now.AddHours(-2), Now, null, ActivationStatus.Ended },
        { Now.AddHours(-1), null, Now.AddMinutes(-5), ActivationStatus.Cancelled },
        { Now.AddHours(1), Now.AddHours(2), Now.AddMinutes(-5), ActivationStatus.Cancelled },
        { Now.AddHours(-2), Now.AddHours(-1), Now.AddHours(-3), ActivationStatus.Cancelled },
    };

    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static readonly TimeZoneInfo Vancouver = TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver");

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_send_now_status_follows_start_end_and_cancel(DateTime start, DateTime? end, DateTime? cancelled, ActivationStatus expected)
    {
        var activation = new TblMessageActivation { StartUtc = start, EndUtc = end, CancelledUtc = cancelled };

        Assert.Equal(expected, ActivationState.Of(activation, Now, [Toronto]));
    }

    public static TheoryData<int, int, int, int, ActivationStatus> WeekdayWindowEdges => new()
    {
        // Mon 19 Oct to Mon 2 Nov 2026, weekdays 09:00 to 17:00 Toronto time (UTC-4 until 1 Nov).
        { 10, 19, 12, 59, ActivationStatus.Scheduled }, // Mon 08:59: inside the range, before the window
        { 10, 19, 13, 0, ActivationStatus.Active },     // Mon 09:00
        { 10, 21, 20, 59, ActivationStatus.Active },    // Wed 16:59
        { 10, 21, 21, 0, ActivationStatus.Scheduled },  // Wed 17:00: the window's end is exclusive
        { 10, 22, 3, 0, ActivationStatus.Scheduled },   // Wed 23:00, at night
        { 10, 24, 16, 0, ActivationStatus.Scheduled },  // Sat 12:00
        { 10, 18, 16, 0, ActivationStatus.Scheduled },  // Sun 12:00, before the range
        { 11, 2, 16, 0, ActivationStatus.Ended },       // Mon 2 Nov 11:00, after the range
    };

    [Theory]
    [MemberData(nameof(WeekdayWindowEdges))]
    public void A_scheduled_send_is_Active_only_while_its_weekly_window_holds_on_a_screen(int month, int day, int hour, int minute,
        ActivationStatus expected)
    {
        var activation = WeekdaysNineToFive();

        Assert.Equal(expected, ActivationState.Of(activation, new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc), [Toronto]));
    }

    [Fact]
    public void A_send_is_Active_while_it_shows_on_any_one_of_its_screens()
    {
        var activation = WeekdaysNineToFive();
        // Wed 21 Oct 21:30 UTC is 17:30 in Toronto, past the window, and 14:30 in Vancouver, inside it.
        var now = new DateTime(2026, 10, 21, 21, 30, 0, DateTimeKind.Utc);

        Assert.Equal(ActivationStatus.Scheduled, ActivationState.Of(activation, now, [Toronto]));
        Assert.Equal(ActivationStatus.Active, ActivationState.Of(activation, now, [Toronto, Vancouver]));
    }

    [Fact]
    public void A_cancelled_scheduled_send_is_Cancelled_inside_its_window()
    {
        var activation = WeekdaysNineToFive();
        activation.CancelledUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(ActivationStatus.Cancelled, ActivationState.Of(activation, new DateTime(2026, 10, 21, 16, 0, 0, DateTimeKind.Utc), [Toronto]));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ended_as_a_query_is_over_without_a_cancel(DateTime start, DateTime? end, DateTime? cancelled, ActivationStatus expected)
    {
        var activation = new TblMessageActivation { StartUtc = start, EndUtc = end, CancelledUtc = cancelled };

        Assert.Equal(expected == ActivationStatus.Ended, new[] { activation }.AsQueryable().Any(ActivationState.Ended(Now)));
    }

    private static TblMessageActivation WeekdaysNineToFive()
    {
        var activation = new TblMessageActivation();
        var timing = ActivationTiming.Resolve(
            new MessageSendTiming.Scheduled(new DateTime(2026, 10, 19, 0, 0, 0), new DateTime(2026, 11, 2, 0, 0, 0),
                new MessageWeeklyWindow(
                    new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                    new TimeOnly(9, 0), new TimeOnly(17, 0))),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), [Toronto, Vancouver]);
        timing.ApplyTo(activation);
        return activation;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Active_or_scheduled_is_the_same_rule_in_memory_and_as_a_query_over_Activations_and_screen_rows(
        DateTime start, DateTime? end, DateTime? cancelled, ActivationStatus expected)
    {
        var activation = new TblMessageActivation { StartUtc = start, EndUtc = end, CancelledUtc = cancelled };
        var target = new TblMessageActivationTarget { MessageActivation = activation };
        var running = expected is ActivationStatus.Active or ActivationStatus.Scheduled;

        Assert.Equal(running, ActivationState.IsActiveOrScheduled(activation, Now));
        Assert.Equal(running, new[] { activation }.AsQueryable().Any(ActivationState.ActiveOrScheduled(Now)));
        Assert.Equal(running, new[] { target }.AsQueryable().Any(ActivationState.TargetActiveOrScheduled(Now)));
    }
}
