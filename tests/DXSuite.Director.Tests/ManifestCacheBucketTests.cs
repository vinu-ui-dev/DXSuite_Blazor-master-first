using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXSuite.Director.Tests;

/// <summary>
/// A display and a location can share a numeric id, and each is cached as its own publication. An
/// invalidation aimed at the wrong type lands on a bucket that does not hold the entry, and the
/// stale publication plays until its TTL.
/// </summary>
public class ManifestCacheBucketTests
{
    private const string Tenant = "tenant-a";

    private static ManifestCacheService NewCache(RecordingWakeNotifier? wake = null)
        => new(new MemoryCache(new MemoryCacheOptions()), NullLogger<ManifestCacheService>.Instance, wake ?? new RecordingWakeNotifier());

    private static CachedManifest Publication(string status) => new() { Status = status };

    [Fact]
    public void A_display_and_a_location_with_the_same_id_are_separate_publications()
    {
        var cache = NewCache();
        cache.SetManifest(Tenant, ManifestBucket.ForLocation(7), Publication("location"));
        cache.SetManifest(Tenant, ManifestBucket.ForDisplay(7), Publication("display"));

        Assert.True(cache.TryGetManifest(Tenant, ManifestBucket.ForLocation(7), out var location));
        Assert.True(cache.TryGetManifest(Tenant, ManifestBucket.ForDisplay(7), out var display));
        Assert.Equal("location", location!.Status);
        Assert.Equal("display", display!.Status);
    }

    [Fact]
    public void Invalidating_a_display_leaves_the_location_with_the_same_id_cached()
    {
        var wake = new RecordingWakeNotifier();
        var cache = NewCache(wake);
        cache.SetManifest(Tenant, ManifestBucket.ForLocation(7), Publication("location"));
        cache.SetManifest(Tenant, ManifestBucket.ForDisplay(7), Publication("display"));

        cache.InvalidateDisplay(Tenant, 7);

        Assert.False(cache.TryGetManifest(Tenant, ManifestBucket.ForDisplay(7), out _));
        Assert.True(cache.TryGetManifest(Tenant, ManifestBucket.ForLocation(7), out _));
        Assert.Equal(new[] { 7 }, wake.Buckets);
    }

    [Fact]
    public void Invalidating_a_location_reaches_every_display_drawing_content_from_it()
    {
        var cache = NewCache();
        cache.SetManifest(Tenant, ManifestBucket.ForDisplay(40), Publication("display 40"));
        cache.SetManifest(Tenant, ManifestBucket.ForDisplay(41), Publication("display 41"));
        cache.SetDisplaySources(Tenant, 40, new[] { 1044 });
        cache.SetDisplaySources(Tenant, 41, new[] { 1046 });

        cache.InvalidateLocation(Tenant, 1044);

        Assert.False(cache.TryGetManifest(Tenant, ManifestBucket.ForDisplay(40), out _));
        Assert.True(cache.TryGetManifest(Tenant, ManifestBucket.ForDisplay(41), out _));
    }

    [Fact]
    public void A_display_republished_without_a_source_stops_being_invalidated_by_it()
    {
        var cache = NewCache();
        cache.SetDisplaySources(Tenant, 40, new[] { 1044 });
        cache.SetDisplaySources(Tenant, 40, new[] { 1046 });
        cache.SetManifest(Tenant, ManifestBucket.ForDisplay(40), Publication("display 40"));

        cache.InvalidateLocation(Tenant, 1044);

        Assert.True(cache.TryGetManifest(Tenant, ManifestBucket.ForDisplay(40), out _));
    }

    [Fact]
    public void Current_entries_counts_only_publications_still_in_the_cache()
    {
        var cache = NewCache();
        cache.SetManifest(Tenant, ManifestBucket.ForLocation(1), Publication("a"), maxAge: TimeSpan.FromMilliseconds(1));
        cache.SetManifest(Tenant, ManifestBucket.ForLocation(2), Publication("b"));
        cache.SetManifest(Tenant, ManifestBucket.ForDisplay(3), Publication("c"));
        cache.InvalidateDisplay(Tenant, 3);

        Thread.Sleep(20);
        cache.TryGetManifest(Tenant, ManifestBucket.ForLocation(1), out _);

        // Eviction callbacks run on the thread pool after the expired read.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (cache.GetStatistics().CurrentEntries != 1 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.Equal(1, cache.GetStatistics().CurrentEntries);
    }

    [Fact]
    public void Retagging_an_asset_changes_its_publication_version()
    {
        var cache = NewCache();
        var before = TaggedPublication("Department", "IT");
        var after = TaggedPublication("Department", "HR");

        cache.SetManifest(Tenant, ManifestBucket.ForLocation(1), before);
        cache.SetManifest(Tenant, ManifestBucket.ForLocation(1), after);

        Assert.NotEqual(before.Version, after.Version);
    }

    [Fact]
    public void A_publication_carrying_no_tags_hashes_as_it_did_before_tags_were_carried()
    {
        var cache = NewCache();
        var noRow = TaggedPublication(null, null);
        noRow.Assets[0].MetadataValues = null;
        var untagged = TaggedPublication(null, null);

        cache.SetManifest(Tenant, ManifestBucket.ForLocation(1), noRow);
        cache.SetManifest(Tenant, ManifestBucket.ForLocation(1), untagged);

        Assert.Equal(noRow.Version, untagged.Version);
    }

    private static CachedManifest TaggedPublication(string? key, string? tag) => new()
    {
        LocationId = 1,
        Status = "Success",
        Assets = new List<PlayerContentItem>
        {
            new()
            {
                FileName = "a.png",
                Url = "/MediaAssets/1/a.png",
                MetadataValues = key == null
                    ? new Dictionary<string, IReadOnlySet<string>>()
                    : new Dictionary<string, IReadOnlySet<string>> { [key] = new HashSet<string> { tag! } }
            }
        }
    };

    private sealed class RecordingWakeNotifier : IPlayerWakeNotifier
    {
        public List<int> Buckets { get; } = new();

        public void NotifyDevice(string tenantId, string deviceId) { }

        public void NotifyManifestBucket(string tenantId, int bucketId) => Buckets.Add(bucketId);
    }
}
