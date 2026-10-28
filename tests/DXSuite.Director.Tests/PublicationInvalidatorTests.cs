using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// The invalidation seam maps each kind of publication-input change to the bucket it lands in. The services
/// that call it after SaveChanges are orchestration over a DbContext and stay uncovered on purpose (15.6).
/// </summary>
public class PublicationInvalidatorTests
{
    private const string Tenant = "tenant-a";

    private static (PublicationInvalidator Seam, FakeManifestCache Cache) Create()
    {
        var cache = new FakeManifestCache();
        var seam = new PublicationInvalidator(cache, new FakeMetadataCache(cache.Calls), new WebPreviewServicePathTests.StubTenantContext { Subdomain = Tenant }, new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PublicationInvalidator>.Instance);
        return (seam, cache);
    }

    [Fact]
    public void ScheduleSave_InvalidatesTheDisplayBucket()
    {
        var (seam, cache) = Create();

        seam.DisplayScheduleChanged(7);

        Assert.Equal(new[] { "display:tenant-a:7" }, cache.Calls);
    }

    [Fact]
    public void DisplayModeChange_InvalidatesTheDisplayBucket()
    {
        var (seam, cache) = Create();

        seam.DisplayChanged(7);

        Assert.Equal(new[] { "display:tenant-a:7" }, cache.Calls);
    }

    [Fact]
    public void LocationFlagChange_InvalidatesSidecarsThenTheLocationBucket()
    {
        var (seam, cache) = Create();

        seam.LocationChanged(22);

        Assert.Equal(new[] { "sidecars:tenant-a:22", "location:tenant-a:22" }, cache.Calls);
    }

    [Fact]
    public void AssetsChanged_InvalidatesEachLocationOnce()
    {
        var (seam, cache) = Create();

        seam.AssetsChanged([22, 23, 22]);

        Assert.Equal(new[] { "sidecars:tenant-a:22", "location:tenant-a:22", "sidecars:tenant-a:23", "location:tenant-a:23" }, cache.Calls);
    }

    private sealed class FakeManifestCache : IManifestCacheService
    {
        public List<string> Calls { get; } = new();

        public void InvalidateLocation(string tenantId, int locationId) => Calls.Add($"location:{tenantId}:{locationId}");
        public void InvalidateDisplay(string tenantId, int displayId) => Calls.Add($"display:{tenantId}:{displayId}");

        public bool TryGetManifest(string tenantId, ManifestBucket bucket, out CachedManifest? manifest)
        {
            manifest = null;
            return false;
        }

        public void SetManifest(string tenantId, ManifestBucket bucket, CachedManifest manifest, TimeSpan? maxAge = null) { }
        public void SetLayoutSources(string tenantId, int layoutLocationId, IEnumerable<int> sourceLocationIds) { }
        public void SetDisplaySources(string tenantId, int displayId, IEnumerable<int> sourceLocationIds) { }
        public void RecordDeliveredVersion(string tenantId, string deviceId, string? version) { }
        public string? GetDeliveredVersion(string tenantId, string deviceId) => null;
        public string ComputeDeliveryVersion(CachedManifest manifest) => "";
        public CacheStatistics GetStatistics() => new();
    }

    private sealed class FakeMetadataCache(List<string> calls) : IMetadataCacheService
    {
        public void InvalidateLocation(string tenantId, int locationId) => calls.Add($"sidecars:{tenantId}:{locationId}");
        public void Invalidate(string filePath) { }
        public ParsedMetadata? GetMetadata(string tenantId, string filePath) => null;
        public Task<ParsedMetadata?> GetMetadataAsync(string tenantId, string filePath) => Task.FromResult<ParsedMetadata?>(null);
    }
}
