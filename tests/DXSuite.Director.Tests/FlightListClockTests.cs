using DXSuite.Aviation.Services.FlightDataEditor;
using Microsoft.Extensions.Configuration;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-10 (B-21): the airport time zone the list window is in comes from the per-instance setting
/// <c>Aviation:FlightDataEditor:AirportTimeZone</c> (an IANA id; Ian, 2026-10-18), not from the server's clock.
/// </summary>
public class FlightListClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_window_is_in_the_configured_airport_time_zone()
    {
        var clock = Clock(("Aviation:FlightDataEditor:AirportTimeZone", "America/Vancouver"));

        var window = clock.CurrentWindow();

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 1, 30, 0, TimeSpan.FromHours(-7)), window.Start);
        Assert.Equal(TimeSpan.FromHours(-7), window.Start.Offset);
        Assert.Null(clock.ConfigurationProblem);
    }

    [Fact]
    public void Each_window_is_taken_from_now()
    {
        var time = new ManualTime(Now);
        var clock = new FlightListClock(Config(("Aviation:FlightDataEditor:AirportTimeZone", "America/Toronto")), time);
        var first = clock.CurrentWindow();

        time.Now = Now.AddMinutes(90);

        Assert.Equal(first.Start.AddMinutes(90), clock.CurrentWindow().Start);
    }

    // The editor still opens on an instance that has not set the zone, on the server's own zone, and says so.
    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("Mars/Olympus_Mons")]
    public void A_missing_or_unknown_zone_falls_back_to_the_servers_zone_and_says_so(string? configured)
    {
        var clock = configured is null ? Clock() : Clock(("Aviation:FlightDataEditor:AirportTimeZone", configured));

        Assert.Equal(TimeZoneInfo.Local.Id, clock.CurrentWindow().AirportTimeZone.Id);
        Assert.Contains("Aviation:FlightDataEditor:AirportTimeZone", clock.ConfigurationProblem);
    }

    private static FlightListClock Clock(params (string Key, string Value)[] settings) =>
        new(Config(settings), new ManualTime(Now));

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
