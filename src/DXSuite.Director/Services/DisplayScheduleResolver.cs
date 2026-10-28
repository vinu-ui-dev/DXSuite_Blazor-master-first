namespace DXSuite.Director.Services;

/// <summary>
/// Reads the display-to-constraint mapping from the access cache.
/// The cache is populated by AccessCacheRefreshService every 60 seconds.
/// </summary>
public class DisplayScheduleResolver : IDisplayScheduleResolver
{
    private readonly IAccessCacheStore _cacheStore;

    public DisplayScheduleResolver(IAccessCacheStore cacheStore)
    {
        _cacheStore = cacheStore;
    }

    public int? ResolveActiveConstraint(string tenantId, int displayId)
    {
        return _cacheStore.GetDisplayConstraint(tenantId, displayId);
    }
}
