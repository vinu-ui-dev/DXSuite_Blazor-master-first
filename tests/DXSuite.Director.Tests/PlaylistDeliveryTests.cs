using System.Diagnostics;
using DXSuite.Director.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace DXSuite.Director.Tests;

/// <summary>
/// A playlist publication is shared by every player at its location, so metadata targeting and the
/// schedule window are both decided per player at delivery. The two filters sit in the same method and
/// can cover for each other, so these assert the exact delivered set, never containment.
/// </summary>
public class PlaylistDeliveryTests
{
    private static readonly DateTime Noon = new(2026, 9, 9, 12, 0, 0);
    private static readonly TimeSpan NineAm = new(9, 0, 0);
    private static readonly TimeSpan FivePm = new(17, 0, 0);
    private static readonly TimeSpan SixPm = new(18, 0, 0);
    private static readonly TimeSpan EightPm = new(20, 0, 0);

    private static readonly Dictionary<string, string> ItPlayer = MetadataWorldBuilder.Player("Department=IT");

    private readonly ITestOutputHelper _output;

    public PlaylistDeliveryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // A1 matches and is inside its window, A2 matches but is outside, A3 is targeted elsewhere but
    // inside, A4 is neither. Targeting broken open surfaces A3, the window broken open surfaces A2,
    // and either broken shut loses A1.
    private static MetadataWorldBuilder TwoByTwo() => new MetadataWorldBuilder()
        .TargetedBy(new MetadataKeyRule("Department"))
        .WithAsset("A1.png", tags: ["Department=IT"], startTime: NineAm, endTime: FivePm)
        .WithAsset("A2.png", tags: ["Department=IT"], startTime: SixPm, endTime: EightPm)
        .WithAsset("A3.png", tags: ["Department=HR"], startTime: NineAm, endTime: FivePm)
        .WithAsset("A4.png", tags: ["Department=HR"], startTime: SixPm, endTime: EightPm);

    [Fact]
    public void Only_content_both_targeted_at_the_player_and_inside_its_window_is_delivered()
    {
        var world = TwoByTwo();

        var delivery = PlaylistDelivery.Resolve(world.BuildPublication(), world.BuildTargeting(), ItPlayer, Noon, imageDurationSeconds: null);

        Assert.Equal(new[] { "A1.png" }, delivery.Assets.Select(a => a.FileName));
    }

    [Fact]
    public void An_unresolvable_time_zone_skips_the_window_but_not_targeting()
    {
        var world = TwoByTwo();

        var delivery = PlaylistDelivery.Resolve(world.BuildPublication(), world.BuildTargeting(), ItPlayer, localNow: null, imageDurationSeconds: null);

        Assert.Equal(new[] { "A1.png", "A2.png" }, delivery.Assets.Select(a => a.FileName));
    }

    [Fact]
    public void Targeted_out_content_contributes_nothing_to_the_weighted_loop()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .Shuffled()
            .WithAsset("A1.png", tags: ["Department=IT"], weight: 2)
            .WithAsset("A3.png", tags: ["Department=HR"], weight: 5)
            .WithAsset("A5.png", weight: 1);

        var delivery = PlaylistDelivery.Resolve(world.BuildPublication(), world.BuildTargeting(), ItPlayer, Noon, imageDurationSeconds: null);

        Assert.Equal(new[] { "A1.png", "A5.png", "A1.png" }, delivery.Assets.Select(a => a.FileName));
    }

    [Fact]
    public void A_player_nothing_is_targeted_at_receives_an_empty_manifest()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("hr.png", tags: ["Department=HR"]);

        var delivery = PlaylistDelivery.Resolve(world.BuildPublication(), world.BuildTargeting(), ItPlayer, Noon, imageDurationSeconds: null);

        Assert.Empty(delivery.Assets);
    }

    [Fact]
    public void Neither_tags_nor_windows_reach_the_wire_and_the_publication_is_untouched()
    {
        var world = TwoByTwo();
        var publication = world.BuildPublication();

        var delivered = Assert.Single(PlaylistDelivery.Resolve(publication, world.BuildTargeting(), ItPlayer, Noon, imageDurationSeconds: null).Assets);

        Assert.Null(delivered.MetadataValues);
        Assert.Null(delivered.StartTime);
        Assert.Null(delivered.EndTime);
        Assert.All(publication.Assets, a => Assert.NotNull(a.MetadataValues));
        Assert.Equal(NineAm, publication.Assets[0].StartTime);
    }

    /// <summary>
    /// Produces a number for the spec's capacity paragraph, once, and asserts nothing. A slow run is a
    /// finding for DXME-188, never a reason to cache or memoise delivery. Excluded from CI by trait.
    /// </summary>
    [Theory]
    [Trait("Category", "Volume")]
    [InlineData(100, false)]
    [InlineData(500, true)]
    public void Volume(int assets, bool shuffle)
    {
        var world = MetadataWorldBuilder.Volume(assets, shuffle);
        var publication = world.BuildPublication();
        var targeting = world.BuildTargeting();
        // The pass DXME-177 measured includes the delivery-version hash, so this one does too.
        var versions = new ManifestCacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<ManifestCacheService>.Instance, new SilentWakeNotifier());
        var players = new[]
        {
            MetadataWorldBuilder.Player(),
            MetadataWorldBuilder.Player("Concourse=Concourse B"),
            MetadataWorldBuilder.Player("Concourse=Concourse C", "Airline=Airline 2", "Language=Lang 1"),
        };

        for (var i = 0; i < 50; i++)
            PlaylistDelivery.Resolve(publication, targeting, players[i % players.Length], Noon, null);

        const int iterations = 2000;
        var delivered = 0L;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var delivery = PlaylistDelivery.Resolve(publication, targeting, players[i % players.Length], Noon, null);
            delivery.Version = versions.ComputeDeliveryVersion(delivery);
            delivered += delivery.Assets.Count;
        }
        stopwatch.Stop();

        _output.WriteLine(
            $"N={assets} shuffle={shuffle}: {stopwatch.Elapsed.TotalMilliseconds * 1000 / iterations:F1} us per delivery pass, " +
            $"{(double)delivered / iterations:F1} items delivered on average");
    }

    private sealed class SilentWakeNotifier : IPlayerWakeNotifier
    {
        public void NotifyDevice(string tenantId, string deviceId) { }

        public void NotifyManifestBucket(string tenantId, int bucketId) { }
    }
}
