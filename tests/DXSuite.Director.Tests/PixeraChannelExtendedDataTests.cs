using DXSuite.Director.Services.Pixera;
using Newtonsoft.Json.Linq;

namespace DXSuite.Director.Tests;

public class PixeraChannelExtendedDataTests
{
    private static readonly DateTime SyncedAt = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    // The current database document as it looks after an admin saved channel settings
    // mid-sync-cycle: transport disabled, plus a handle map written by content sync.
    private static string AdminSavedDocument() => JObject.FromObject(new
    {
        pixeraLayerSettings = new
        {
            assignedLayerPaths = new[] { "Screen 1.Layer 1" },
            transportMode = 2,
            transportLoop = false,
            transportEnabled = false,
        },
        pixeraHandleMap = new Dictionary<string, object>
        {
            ["a.mp4"] = new { UncPath = @"\\share\a.mp4", PixeraPath = "Folder/a.mp4" },
        },
    }).ToString(Newtonsoft.Json.Formatting.None);

    [Fact]
    public void WithLayerSyncTimestamp_PreservesAdminOwnedSettings()
    {
        // Reverting transportEnabled=false would re-enable live layer takeover on the
        // device — the destructive race this merge exists to prevent.
        var merged = JObject.Parse(
            PixeraChannelExtendedData.WithLayerSyncTimestamp(AdminSavedDocument(), SyncedAt));

        var settings = (JObject)merged["pixeraLayerSettings"]!;
        Assert.False(settings["transportEnabled"]!.Value<bool>());
        Assert.Equal(2, settings["transportMode"]!.Value<int>());
        Assert.False(settings["transportLoop"]!.Value<bool>());
        Assert.Equal("Screen 1.Layer 1", settings["assignedLayerPaths"]![0]!.ToString());
        Assert.Equal(SyncedAt, settings["lastSyncedAt"]!.Value<DateTime>());
    }

    [Fact]
    public void WithLayerSyncTimestamp_PreservesHandleMap()
    {
        var merged = JObject.Parse(
            PixeraChannelExtendedData.WithLayerSyncTimestamp(AdminSavedDocument(), SyncedAt));

        Assert.Equal("Folder/a.mp4", merged["pixeraHandleMap"]!["a.mp4"]!["PixeraPath"]!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not valid json")]
    public void WithLayerSyncTimestamp_UnusableDocument_StartsFresh(string? extendedData)
    {
        var merged = JObject.Parse(
            PixeraChannelExtendedData.WithLayerSyncTimestamp(extendedData, SyncedAt));

        Assert.Equal(SyncedAt, merged["pixeraLayerSettings"]!["lastSyncedAt"]!.Value<DateTime>());
    }

    [Fact]
    public void WithHandleMap_PreservesAdminOwnedSettings()
    {
        var newMap = JObject.FromObject(new Dictionary<string, object>
        {
            ["b.mp4"] = new { UncPath = @"\\share\b.mp4", PixeraPath = "Folder/b.mp4" },
        });

        var merged = JObject.Parse(
            PixeraChannelExtendedData.WithHandleMap(AdminSavedDocument(), newMap));

        var settings = (JObject)merged["pixeraLayerSettings"]!;
        Assert.False(settings["transportEnabled"]!.Value<bool>());
        Assert.Equal("Screen 1.Layer 1", settings["assignedLayerPaths"]![0]!.ToString());
    }

    [Fact]
    public void WithHandleMap_ReplacesMapWholesale()
    {
        var newMap = JObject.FromObject(new Dictionary<string, object>
        {
            ["b.mp4"] = new { UncPath = @"\\share\b.mp4", PixeraPath = "Folder/b.mp4" },
        });

        var merged = JObject.Parse(
            PixeraChannelExtendedData.WithHandleMap(AdminSavedDocument(), newMap));

        var map = (JObject)merged["pixeraHandleMap"]!;
        Assert.Null(map["a.mp4"]);
        Assert.Equal("Folder/b.mp4", map["b.mp4"]!["PixeraPath"]!.ToString());
    }
}
