using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class TimeZoneEmissionTests
{
    [Theory]
    [InlineData("Operator", true)]
    [InlineData("Seeded", false)]
    [InlineData("Discovered", false)]
    [InlineData("operator", false)]
    [InlineData(null, false)]
    public void Only_an_operator_confirmed_zone_is_sent(string? source, bool expected)
        => Assert.Equal(expected, DeviceProvisioning.IsOperatorConfirmed(source));

    [Fact]
    public void The_catalog_carries_the_complete_SetTimeZone_argument()
    {
        Assert.Equal("POSIX:MST7", TimeZoneCatalog.FindSetTimeZoneArgument("America/Phoenix"));
        Assert.Equal("POSIX:CST6CDT,M3.2.0,M11.1.0", TimeZoneCatalog.FindSetTimeZoneArgument("America/Chicago"));
        Assert.Equal("POSIX:MST7MDT,M3.2.0,M11.1.0", TimeZoneCatalog.FindSetTimeZoneArgument("America/Denver"));
    }

    // A device carrying a zone nobody curated is sent nothing rather than a constructed string.
    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("UNSET")]
    [InlineData(null)]
    public void An_off_list_zone_emits_nothing(string? ianaId)
        => Assert.Null(TimeZoneCatalog.FindSetTimeZoneArgument(ianaId));

    // Every literal on the list has to be one .NET can also resolve, because the server resolves
    // windows against the same row's IANA id.
    [Fact]
    public void Every_curated_zone_resolves_and_carries_a_posix_literal()
    {
        Assert.All(TimeZoneCatalog.Options, option =>
        {
            Assert.StartsWith("POSIX:", option.SetTimeZoneArgument);
            Assert.False(string.IsNullOrWhiteSpace(option.Label));
            Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(option.IanaId));
        });
    }

    // The three deployment zones, checked against the offsets DXME-196 measured on hardware.
    [Theory]
    [InlineData("America/Chicago", -6, -5)]
    [InlineData("America/Denver", -7, -6)]
    [InlineData("America/Phoenix", -7, -7)]
    public void The_deployment_zones_have_the_offsets_the_bench_measured(string ianaId, int january, int july)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);

        Assert.Equal(january, zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).TotalHours);
        Assert.Equal(july, zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)).TotalHours);
    }
}
