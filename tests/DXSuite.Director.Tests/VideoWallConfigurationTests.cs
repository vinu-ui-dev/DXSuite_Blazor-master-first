using System.Text.Json;
using DXSuite.Director.Models;

namespace DXSuite.Director.Tests;

// Logo roles live in TblLocation.ExtendedData JSON; both directions of compatibility
// matter: configs written before roles existed must load cleanly (zones fall back to
// the legacy default role), and roled configs must round-trip through the camelCase
// serializer the modal uses.
public class VideoWallConfigurationTests
{
    private static readonly JsonSerializerOptions WriteOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions ReadOpts  = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void LegacyConfig_WithoutRoleFields_LoadsWithDefaults()
    {
        const string legacy = """
            {"layout":{"groups":[{"groupName":"Logo Zone 1","role":"logo","displayIds":[42]}]},
             "contentScaleFactor":0.4}
            """;

        var config = JsonSerializer.Deserialize<VideoWallConfiguration>(legacy, ReadOpts)!;

        Assert.Empty(config.LogoRoles);
        Assert.Null(config.Layout.Groups.Single().LogoRoleKey);
    }

    [Fact]
    public void AllocationBounds_DefaultNull_AndRoundTrip()
    {
        var legacy = JsonSerializer.Deserialize<PanoramaGroup>(
            """{"groupName":"Row 1","role":"counter-row"}""", ReadOpts)!;
        Assert.Null(legacy.AllocationLeftPx);
        Assert.Null(legacy.AllocationRightPx);

        var json = JsonSerializer.Serialize(
            new PanoramaGroup { AllocationLeftPx = 100, AllocationRightPx = 2500 }, WriteOpts);
        var back = JsonSerializer.Deserialize<PanoramaGroup>(json, ReadOpts)!;
        Assert.Equal(100, back.AllocationLeftPx);
        Assert.Equal(2500, back.AllocationRightPx);
    }

    [Fact]
    public void ContentTopOffset_DefaultsToZero_AndRoundTrips()
    {
        var legacy = JsonSerializer.Deserialize<VideoWallConfiguration>("{}", ReadOpts)!;
        Assert.Equal(0, legacy.ContentTopOffsetPx);

        var json = JsonSerializer.Serialize(new VideoWallConfiguration { ContentTopOffsetPx = 300 }, WriteOpts);
        var back = JsonSerializer.Deserialize<VideoWallConfiguration>(json, ReadOpts)!;
        Assert.Equal(300, back.ContentTopOffsetPx);
    }

    [Fact]
    public void RoledConfig_RoundTrips_ThroughCamelCaseSerializer()
    {
        var config = new VideoWallConfiguration
        {
            LogoRoles =
            [
                new LogoRoleDefinition { Key = "counter-logos", Name = "Counter Logos", SortOrder = 0 },
                new LogoRoleDefinition { Key = "accent-logos",  Name = "Accent Logos",  SortOrder = 1 },
            ],
        };
        config.Layout.Groups.Add(new PanoramaGroup { GroupName = "Accent 1", Role = "logo", LogoRoleKey = "accent-logos" });
        config.Layout.Groups.Add(new PanoramaGroup { GroupName = "Legacy",   Role = "logo", LogoRoleKey = null });

        var json = JsonSerializer.Serialize(config, WriteOpts);
        var back = JsonSerializer.Deserialize<VideoWallConfiguration>(json, ReadOpts)!;

        Assert.Equal(["counter-logos", "accent-logos"], back.LogoRoles.OrderBy(r => r.SortOrder).Select(r => r.Key));
        Assert.Equal("accent-logos", back.Layout.Groups[0].LogoRoleKey);
        Assert.Null(back.Layout.Groups[1].LogoRoleKey);
    }
}
