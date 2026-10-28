using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-10 (B-21): the Flight Data Editor's list window runs 6 hours back to 18 hours forward from now in the airport
/// time zone, and a flight is in it by its best-known time: actual, else estimated, else scheduled.
/// </summary>
public class FlightListWindowTests
{
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    // 10:30 in Toronto (EDT, UTC-4).
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_window_runs_6_hours_back_to_18_hours_forward_in_airport_time()
    {
        var window = FlightListWindow.At(Now, Toronto);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 4, 30, 0, TimeSpan.FromHours(-4)), window.Start);
        Assert.Equal(TimeSpan.FromHours(-4), window.Start.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 4, 30, 0, TimeSpan.FromHours(-4)), window.End);
        Assert.Equal(TimeSpan.FromHours(-4), window.End.Offset);
    }

    // Each end carries the offset the airport had at that instant, so a window across the autumn change is still
    // 24 elapsed hours.
    [Fact]
    public void Each_end_of_the_window_carries_the_airports_offset_at_that_instant()
    {
        // 05:00 EST on the morning summer time ends (02:00 EDT became 01:00 EST).
        var window = FlightListWindow.At(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), Toronto);

        Assert.Equal("2026-11-01T00:00:00-04:00", CoreApiClient.WindowTime(window.Start));
        Assert.Equal("2026-11-01T23:00:00-05:00", CoreApiClient.WindowTime(window.End));
    }

    [Fact]
    public void The_window_is_cut_to_whole_seconds()
    {
        var window = FlightListWindow.At(Now.AddMilliseconds(987), Toronto);

        Assert.Equal("2026-10-19T04:30:00-04:00", CoreApiClient.WindowTime(window.Start));
    }

    [Fact]
    public void The_modals_get_the_window_as_airport_wall_clock_times()
    {
        var window = FlightListWindow.At(Now, Toronto);

        Assert.Equal(new DateTime(2026, 10, 19, 4, 30, 0), window.LocalStart);
        Assert.Equal(new DateTime(2026, 10, 20, 4, 30, 0), window.LocalEnd);
        Assert.Equal(DateTimeKind.Unspecified, window.LocalStart.Kind);
    }

    // CoreAPI's flight times carry no offset: they are airport wall-clock times.
    public static TheoryData<string, DateTime?, DateTime?, DateTime?, bool> ArrivalsAtTheEdges => new()
    {
        { "actual on the start", Local(4, 30), Local(3, 0), Local(3, 0), true },
        { "actual a second before the start, estimated and scheduled inside", Local(4, 29, 59), Local(5, 0), Local(5, 0), false },
        { "estimated on the start, no actual", null, Local(4, 30), Local(3, 0), true },
        { "estimated a second before the start, scheduled inside", null, Local(4, 29, 59), Local(6, 0), false },
        { "scheduled on the start, nothing else", null, null, Local(4, 30), true },
        { "scheduled a second before the start", null, null, Local(4, 29, 59), false },
        { "actual a second before the end", Next(4, 29, 59), Next(6, 0), Next(6, 0), true },
        { "actual on the end", Next(4, 30), Local(12, 0), Local(12, 0), false },
        { "estimated a second before the end, scheduled after it", null, Next(4, 29, 59), Next(5, 0), true },
        { "estimated on the end, scheduled inside", null, Next(4, 30), Local(12, 0), false },
        { "scheduled a second before the end", null, null, Next(4, 29, 59), true },
        { "scheduled on the end", null, null, Next(4, 30), false },
        { "no time at all", null, null, null, false },
    };

    [Theory]
    [MemberData(nameof(ArrivalsAtTheEdges))]
    public void An_arrival_is_in_the_window_by_its_actual_else_estimated_else_scheduled_time(
        string because, DateTime? actual, DateTime? estimated, DateTime? scheduled, bool inWindow)
    {
        var window = FlightListWindow.At(Now, Toronto);
        var flight = new FEArrivalFlight { ActualTime = actual, EstimatedTime = estimated, ScheduledTime = scheduled };

        Assert.True(inWindow == window.Contains(flight), because);
    }

    [Theory]
    [MemberData(nameof(ArrivalsAtTheEdges))]
    public void A_departure_is_in_the_window_by_its_actual_else_estimated_else_scheduled_time(
        string because, DateTime? actual, DateTime? estimated, DateTime? scheduled, bool inWindow)
    {
        var window = FlightListWindow.At(Now, Toronto);
        var flight = new FEDepartureFlight { ActualTime = actual, EstimatedTime = estimated, ScheduledTime = scheduled };

        Assert.True(inWindow == window.Contains(flight), because);
    }

    // An edited time is the flight's best-known time for that field.
    [Fact]
    public void An_edited_time_places_the_flight_ahead_of_the_feeds_time()
    {
        var window = FlightListWindow.At(Now, Toronto);
        var arrival = new FEArrivalFlight
        {
            EstimatedTime = Local(12, 0),
            ScheduledTime = Local(12, 0),
            overrideFlightData = new OverrideArrivalFlight { ETA = Local(4, 29, 59) },
        };
        var departure = new FEDepartureFlight
        {
            ScheduledTime = Local(12, 0),
            overrideFlightData = new OverrideDepartureFlight { ATD = Next(4, 30) },
        };

        Assert.False(window.Contains(arrival));
        Assert.False(window.Contains(departure));
    }

    [Fact]
    public void A_time_given_with_an_offset_is_placed_by_its_instant()
    {
        var window = FlightListWindow.At(Now, Toronto);
        // 08:29:59 UTC is 04:29:59 in Toronto, a second before the start; 08:30 UTC is on it.
        var before = new FEArrivalFlight { ScheduledTime = new DateTime(2026, 10, 19, 8, 29, 59, DateTimeKind.Utc) };
        var on = new FEArrivalFlight { ScheduledTime = new DateTime(2026, 10, 19, 8, 30, 0, DateTimeKind.Utc) };

        Assert.False(window.Contains(before));
        Assert.True(window.Contains(on));
    }

    private static DateTime Local(int hour, int minute, int second = 0) => new(2026, 10, 19, hour, minute, second);

    private static DateTime Next(int hour, int minute, int second = 0) => new(2026, 10, 20, hour, minute, second);
}
