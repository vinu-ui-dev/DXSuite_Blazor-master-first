using System.Text.Json;
using DXSuite.Director.Models.ZoneContent;
using DXSuite.Director.Services;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Tests;

/// <summary>
/// A layout zone that sources a playlist location must play what that location's own devices would
/// play, and nothing belonging to another device. The layout publication is cached once per layout
/// and shared by every device polling it, so approval is decided when it is built and everything
/// that depends on the asking device is decided at delivery.
/// </summary>
public class LayoutDeliveryTests
{
    private const int SourceLocationId = 1044;

    [Theory]
    [InlineData(ApprovalStatus.Approved, true)]
    [InlineData(ApprovalStatus.Pending, false)]
    [InlineData(ApprovalStatus.Declined, false)]
    [InlineData(ApprovalStatus.Withdrawn, false)]
    public void A_zone_publishes_source_content_by_its_approval_status(ApprovalStatus status, bool expected)
        => Assert.Equal(expected, LayoutZoneRules.IsPublished(enableContentApproval: true, approvalStatus: (int)status));

    [Fact]
    public void A_zone_publishes_source_content_with_no_approval_row_as_legacy_content()
        => Assert.True(LayoutZoneRules.IsPublished(enableContentApproval: true, approvalStatus: null));

    [Fact]
    public void A_zone_ignores_approval_where_the_source_location_has_it_switched_off()
        => Assert.True(LayoutZoneRules.IsPublished(enableContentApproval: false, approvalStatus: (int)ApprovalStatus.Declined));

    [Fact]
    public void Two_devices_at_one_layout_each_receive_only_the_content_targeted_at_them()
    {
        var published = new[] { TargetedZone(ItHrAndUntagged()) };
        var targeting = TargetedByDepartment();

        var it = LayoutZoneRules.ResolvePlaylistZones(published, targeting, MetadataWorldBuilder.Player("Department=IT"), "device-it");
        var hr = LayoutZoneRules.ResolvePlaylistZones(published, targeting, MetadataWorldBuilder.Player("Department=HR"), "device-hr");

        Assert.Equal(new int?[] { 1, 3 }, Assert.Single(it).Content.Select(c => c.AssetId));
        Assert.Equal(new int?[] { 2, 3 }, Assert.Single(hr).Content.Select(c => c.AssetId));
        Assert.Equal(3, published[0].Content.Count);
    }

    [Fact]
    public void Every_zone_sourcing_one_location_receives_the_same_targeted_content()
    {
        var published = new[] { TargetedZone(ItHrAndUntagged(), zoneNumber: 1), TargetedZone(ItHrAndUntagged(), zoneNumber: 7) };

        var delivered = LayoutZoneRules.ResolvePlaylistZones(published, TargetedByDepartment(), MetadataWorldBuilder.Player("Department=IT"), "device-it");

        Assert.All(delivered, zone => Assert.Equal(new int?[] { 1, 3 }, zone.Content.Select(c => c.AssetId)));
        Assert.Equal(new[] { 1, 7 }, delivered.Select(z => z.ZoneNumber));
    }

    [Fact]
    public void A_source_with_targeting_switched_off_delivers_every_published_item()
    {
        var published = new[] { TargetedZone(ItHrAndUntagged()) };
        var switchedOff = new Dictionary<int, LocationTargeting>
        {
            [SourceLocationId] = new MetadataWorldBuilder().TargetedBy(new MetadataKeyRule("Department")).WithTargetingSwitchedOff().BuildTargeting()
        };

        var delivered = LayoutZoneRules.ResolvePlaylistZones(published, switchedOff, MetadataWorldBuilder.Player("Department=IT"), "device-it");

        Assert.Equal(new int?[] { 1, 2, 3 }, Assert.Single(delivered).Content.Select(c => c.AssetId));
    }

    [Fact]
    public void A_zone_with_no_targeting_source_delivers_every_published_item()
    {
        var zone = TargetedZone(ItHrAndUntagged());
        zone.TargetingLocationId = null;

        var delivered = LayoutZoneRules.ResolvePlaylistZones(new[] { zone }, TargetedByDepartment(), MetadataWorldBuilder.Player("Department=IT"), "device-it");

        Assert.Equal(new int?[] { 1, 2, 3 }, Assert.Single(delivered).Content.Select(c => c.AssetId));
    }

    [Fact]
    public void Card_zones_are_left_for_the_caller_to_fill_in()
    {
        var card = new ZoneDisplayContent { DisplayId = 1187, ZoneNumber = 2, ZoneType = "card" };

        var delivered = LayoutZoneRules.ResolvePlaylistZones(new[] { card }, TargetedByDepartment(), deviceMetadata: null, "device-it");

        Assert.Same(card, Assert.Single(delivered));
    }

    [Fact]
    public void Each_device_receives_web_content_carrying_its_own_identity()
    {
        var published = new[] { TargetedZone(new ZoneContentItem { Url = "https://example.com/board", ContentType = "web", Duration = 30, MetadataValues = Tags() }) };
        var targeting = new Dictionary<int, LocationTargeting>();

        var lobby = LayoutZoneRules.ResolvePlaylistZones(published, targeting, new Dictionary<string, string> { ["Department"] = "IT" }, "device-lobby");
        var gate = LayoutZoneRules.ResolvePlaylistZones(published, targeting, new Dictionary<string, string> { ["Department"] = "HR" }, "device-gate");

        Assert.Equal("https://example.com/board?Department=IT&deviceId=device-lobby", Assert.Single(Assert.Single(lobby).Content).Url);
        Assert.Equal("https://example.com/board?Department=HR&deviceId=device-gate", Assert.Single(Assert.Single(gate).Content).Url);
        Assert.Equal("https://example.com/board", Assert.Single(published[0].Content).Url);
    }

    [Fact]
    public void A_delivered_zone_carries_no_targeting_source_or_tags_on_the_wire()
    {
        var published = new[] { TargetedZone(ItHrAndUntagged()) };

        var delivered = Assert.Single(LayoutZoneRules.ResolvePlaylistZones(published, TargetedByDepartment(), MetadataWorldBuilder.Player("Department=IT"), "device-it"));

        Assert.Null(delivered.TargetingLocationId);
        Assert.All(delivered.Content, item => Assert.Null(item.MetadataValues));
        Assert.DoesNotContain("Department", JsonSerializer.Serialize(delivered));
    }

    [Fact]
    public void A_published_zone_with_tags_serializes_for_its_version_hash()
        => Assert.Contains("\"IT\"", JsonSerializer.Serialize(TargetedZone(ItHrAndUntagged())));

    private static Dictionary<int, LocationTargeting> TargetedByDepartment() => new()
    {
        [SourceLocationId] = new MetadataWorldBuilder().TargetedBy(new MetadataKeyRule("Department")).BuildTargeting()
    };

    private static ZoneContentItem[] ItHrAndUntagged() =>
    [
        new() { AssetId = 1, Url = "/MediaAssets/1044/it.png", ContentType = "image", MetadataValues = Tags("Department=IT") },
        new() { AssetId = 2, Url = "/MediaAssets/1044/hr.png", ContentType = "image", MetadataValues = Tags("Department=HR") },
        new() { AssetId = 3, Url = "/MediaAssets/1044/all.png", ContentType = "image", MetadataValues = Tags() }
    ];

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Tags(params string[] pairs) =>
        MetadataSubjectResolver.DeviceSubject(MetadataWorldBuilder.Player(pairs));

    private static ZoneDisplayContent TargetedZone(params ZoneContentItem[] items) => TargetedZone(items, zoneNumber: 6);

    private static ZoneDisplayContent TargetedZone(ZoneContentItem[] items, int zoneNumber) => new()
    {
        ZoneNumber = zoneNumber,
        ZoneName = "Playlist 1044",
        ZoneType = "playlist",
        Layout = new ZoneLayoutPosition { X = 0, Y = 0, Width = 1920, Height = 1080 },
        Content = items.ToList(),
        TargetingLocationId = SourceLocationId
    };
}
