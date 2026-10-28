using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class ScheduleEventSelectionTests
{
    private static CachedDisplayEvent Event(
        int id, bool isDefault = false,
        DateTime? startDate = null, DateTime? endDate = null,
        TimeOnly? startTime = null, TimeOnly? endTime = null,
        bool wednesday = false)
        => new()
        {
            ScheduleEventId = id,
            DefaultContent = isDefault,
            StartDate = startDate,
            EndDate = endDate,
            StartTime = startTime,
            EndTime = endTime,
            Wednesday = wednesday
        };

    private static readonly DateTime WednesdayNoon = new(2026, 9, 9, 12, 0, 0);

    private static CachedDisplayEvent? Pick(IEnumerable<CachedDisplayEvent> events, DateTime at)
        => ScheduleEventSelection.PickActive(events, at, e => e.DefaultContent);

    [Fact]
    public void The_last_matching_event_wins()
    {
        var events = new[] { Event(1), Event(2), Event(3, isDefault: true) };

        Assert.Equal(2, Pick(events, WednesdayNoon)!.ScheduleEventId);
    }

    [Fact]
    public void Nothing_matching_falls_back_to_the_default_event()
    {
        var events = new[]
        {
            Event(1, startDate: new DateTime(2026, 1, 1), endDate: new DateTime(2026, 1, 31)),
            Event(2, isDefault: true)
        };

        Assert.Equal(2, Pick(events, WednesdayNoon)!.ScheduleEventId);
    }

    [Fact]
    public void With_no_default_event_nothing_matching_means_nothing()
        => Assert.Null(Pick(new[] { Event(1, endDate: new DateTime(2026, 1, 31)) }, WednesdayNoon));

    [Fact]
    public void Date_bounds_include_their_last_day()
    {
        var events = new[] { Event(1, startDate: new DateTime(2026, 9, 9), endDate: new DateTime(2026, 9, 9)) };

        Assert.NotNull(Pick(events, WednesdayNoon));
        Assert.Null(Pick(events, WednesdayNoon.AddDays(1)));
    }

    [Fact]
    public void A_selected_day_restricts_the_event_and_no_day_selected_does_not()
    {
        Assert.NotNull(Pick(new[] { Event(1, wednesday: true) }, WednesdayNoon));
        Assert.Null(Pick(new[] { Event(1, wednesday: true) }, WednesdayNoon.AddDays(1)));
        Assert.NotNull(Pick(new[] { Event(1) }, WednesdayNoon.AddDays(1)));
    }

    [Fact]
    public void The_time_window_follows_the_shared_rule()
    {
        var events = new[] { Event(1, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(12, 0)) };

        Assert.NotNull(Pick(events, WednesdayNoon.AddHours(-1)));
        Assert.Null(Pick(events, WednesdayNoon));
    }

    // The whole point of resolving per device: one publication, two answers.
    [Fact]
    public void Two_zones_can_get_different_events_from_one_publication()
    {
        var events = new[]
        {
            Event(1, startTime: new TimeOnly(8, 0), endTime: new TimeOnly(12, 0)),
            Event(2, isDefault: true)
        };

        Assert.Equal(1, Pick(events, new DateTime(2026, 9, 9, 11, 0, 0))!.ScheduleEventId);
        Assert.Equal(2, Pick(events, new DateTime(2026, 9, 9, 13, 0, 0))!.ScheduleEventId);
    }
}
