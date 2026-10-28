using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// The invalidation seam for publication inputs (spec section 8.5). Every write that changes what a
/// cached publication would contain calls one of these after its SaveChanges, named for what changed so a
/// writer never has to know which bucket a change lands in. Publications live for thirty minutes, so a
/// write that skips this reaches screens up to half an hour late.
/// </summary>
public interface IPublicationInvalidator
{
    /// <summary>A display's own settings, override or mode changed.</summary>
    void DisplayChanged(int displayId);

    /// <summary>A display's schedule events were added, edited or removed.</summary>
    void DisplayScheduleChanged(int displayId);

    /// <summary>A display was deleted, or moved away from a location.</summary>
    void DisplayRemoved(int displayId);

    /// <summary>
    /// A location's playback settings, layout configuration or content changed. Reaches every layout
    /// and display publication drawing on that location.
    /// </summary>
    void LocationChanged(int locationId);

    /// <summary>Asset rows changed at these locations: added, soft deleted, renamed, retagged or swapped.</summary>
    void AssetsChanged(IEnumerable<int> locationIds);
}

public class PublicationInvalidator : IPublicationInvalidator
{
    private readonly IManifestCacheService _manifestCache;
    private readonly IMetadataCacheService _metadataCache;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PublicationInvalidator> _logger;

    public PublicationInvalidator(
        IManifestCacheService manifestCache,
        IMetadataCacheService metadataCache,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PublicationInvalidator> logger)
    {
        _manifestCache = manifestCache;
        _metadataCache = metadataCache;
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void DisplayChanged(int displayId) => ForTenant(t => _manifestCache.InvalidateDisplay(t, displayId), "display", displayId);

    public void DisplayScheduleChanged(int displayId) => ForTenant(t => _manifestCache.InvalidateDisplay(t, displayId), "display", displayId);

    public void DisplayRemoved(int displayId) => ForTenant(t => _manifestCache.InvalidateDisplay(t, displayId), "display", displayId);

    // Sidecars drop before the publication, or its rebuild would reparse stale play order from the sidecar
    // cache and hold it for the publication's lifetime. The folder watcher does the same, but can miss events.
    public void LocationChanged(int locationId) => ForTenant(t =>
    {
        _metadataCache.InvalidateLocation(t, locationId);
        _manifestCache.InvalidateLocation(t, locationId);
    }, "location", locationId);

    public void AssetsChanged(IEnumerable<int> locationIds)
    {
        foreach (var locationId in locationIds.Distinct())
            LocationChanged(locationId);
    }

    // A service resolved in a child scope sees an unpopulated tenant context, so fall back to the request's,
    // as the Director DbContext factory does. With neither there is no tenant key to invalidate under.
    private void ForTenant(Action<string> invalidate, string bucketType, int id)
    {
        var tenant = _tenantContext.IsPopulated
            ? _tenantContext
            : _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();

        if (tenant?.IsPopulated != true)
        {
            _logger.LogWarning("No tenant resolved; {BucketType} {Id} publication not invalidated", bucketType, id);
            return;
        }

        invalidate(tenant.TenantId);
    }
}
