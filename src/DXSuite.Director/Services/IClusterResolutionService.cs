using DXSuite.Director.Services.WallPlatforms;

namespace DXSuite.Director.Services;

public interface IClusterResolutionService
{
    /// <summary>
    /// Returns one ResolvedCluster per airline across all AllowAirlineAllocation groups at the
    /// given VideoWall location. Runs for the same airline in multiple groups are merged into a
    /// single cluster with a multi-span ClusterKey. Unassigned gaps produce a per-group cluster
    /// with null UserGroupId. Results are cached for one minute per tenant/location.
    /// </summary>
    Task<IReadOnlyList<ResolvedCluster>> ResolveClustersForLocationAsync(
        string tenantId, int locationId, DateTime atTime);

    /// <summary>
    /// Evict the cluster cache for a location. Call when a schedule event is saved.
    /// </summary>
    void InvalidateLocation(string tenantId, int locationId);
}
