using DXSuite.Shared.Authorization;

namespace DXSuite.Director.Services;

/// <summary>
/// Thread-safe cache for resolved access data and display-to-constraint mappings.
/// Populated by AccessCacheRefreshService on a 60-second cycle.
/// All keys are prefixed with tenantId to prevent cross-tenant data bleed.
/// </summary>
public interface IAccessCacheStore
{
    /// <summary>
    /// Gets the cached resolved access for a user at a location.
    /// Returns null if not cached (caller should fall back to direct DB resolution).
    /// </summary>
    ResolvedAccess? GetAccess(string tenantId, int userId, int locationId);

    /// <summary>
    /// Gets all cached location access entries for a user.
    /// </summary>
    Dictionary<int, ResolvedAccess?> GetAccessMap(string tenantId, int userId);

    /// <summary>
    /// Stores resolved access for a user at a location.
    /// </summary>
    void SetAccess(string tenantId, int userId, int locationId, ResolvedAccess? access);

    /// <summary>
    /// Clears all cached access entries for a user.
    /// </summary>
    void ClearUser(string tenantId, int userId);

    /// <summary>
    /// Gets the active constraint ID for a display at the current time.
    /// Returns null if no constraint-scoped schedule is active.
    /// </summary>
    int? GetDisplayConstraint(string tenantId, int displayId);

    /// <summary>
    /// Stores the active constraint for a display.
    /// </summary>
    void SetDisplayConstraint(string tenantId, int displayId, int? constraintId);

    /// <summary>
    /// Clears all display constraint entries for a tenant.
    /// </summary>
    void ClearDisplayConstraints(string tenantId);

    /// <summary>
    /// Clears the entire cache across all tenants.
    /// </summary>
    void ClearAll();
}
