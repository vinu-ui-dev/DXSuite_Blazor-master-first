using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class PlaylistWindowTests
{
    private static PlayerContentItem Asset(
        string? days = null, DateTime? startDate = null, DateTime? endDate = null,
        TimeSpan? startTime = null, TimeSpan? endTime = null)
        => new()
        {
            FileName = "clip.mp4",
            Days = days,
            StartDate = startDate,
            EndDate = endDate,
            StartTime = startTime,
            EndTime = endTime
        };

    private static readonly DateTime WednesdayNoon = new(2026, 9, 9, 12, 0, 0);

    [Fact]
    public void An_asset_with_no_window_is_always_active()
        => Assert.True(PlaylistWindow.IsActiveAt(Asset(), WednesdayNoon));

    [Theory]
    [InlineData("We", true)]
    [InlineData("Mo,We,Fr", true)]
    [InlineData("we", true)]
    [InlineData("Mo,Tu", false)]
    public void The_day_list_is_read_against_the_local_day(string days, bool expected)
        => Assert.Equal(expected, PlaylistWindow.IsActiveAt(Asset(days: days), WednesdayNoon));

    // The device's day is the point of the change: at this instant a Chicago player is still on
    // Wednesday while a player one zone east has already turned over.
    [Fact]
    public void The_same_instant_can_be_two_different_days()
    {
        var lateWednesday = new DateTime(2026, 9, 9, 23, 30, 0);
        var earlyThursday = lateWednesday.AddHours(1);

        Assert.True(PlaylistWindow.IsActiveAt(Asset(days: "We"), lateWednesday));
        Assert.False(PlaylistWindow.IsActiveAt(Asset(days: "We"), earlyThursday));
    }

    [Fact]
    public void Date_bounds_are_inclusive_of_their_last_day()
    {
        var asset = Asset(startDate: new DateTime(2026, 9, 1), endDate: new DateTime(2026, 9, 9));

        Assert.True(PlaylistWindow.IsActiveAt(asset, WednesdayNoon));
        Assert.False(PlaylistWindow.IsActiveAt(asset, WednesdayNoon.AddDays(1)));
        Assert.False(PlaylistWindow.IsActiveAt(asset, new DateTime(2026, 8, 31, 12, 0, 0)));
    }

    [Fact]
    public void The_time_window_follows_the_shared_rule()
    {
        var asset = Asset(startTime: new TimeSpan(9, 0, 0), endTime: new TimeSpan(12, 0, 0));

        Assert.True(PlaylistWindow.IsActiveAt(asset, WednesdayNoon.AddHours(-1)));
        Assert.False(PlaylistWindow.IsActiveAt(asset, WednesdayNoon));
    }

    // One malformed sidecar must not take a location's whole manifest down.
    [Fact]
    public void A_time_outside_a_day_reads_as_no_bound()
    {
        var asset = Asset(startTime: TimeSpan.FromHours(30), endTime: TimeSpan.FromHours(-2));

        Assert.True(PlaylistWindow.IsActiveAt(asset, WednesdayNoon));
    }
}
