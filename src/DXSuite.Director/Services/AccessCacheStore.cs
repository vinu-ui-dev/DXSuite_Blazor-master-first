using System.Collections.Concurrent;
using DXSuite.Shared.Authorization;

namespace DXSuite.Director.Services;

/// <summary>
/// In-memory cache for resolved access and display constraint mappings.
/// Thread-safe via ConcurrentDictionary. All keys are tenant-prefixed.
/// </summary>
public class AccessCacheStore : IAccessCacheStore
{
    // Key: "tenantId:userId:locationId" → ResolvedAccess
    private readonly ConcurrentDictionary<string, ResolvedAccess?> _accessCache = new();

    // Key: "tenantId:displayId" → active constraintId (null = no constraint)
    private readonly ConcurrentDictionary<string, int?> _displayConstraintCache = new();

    public ResolvedAccess? GetAccess(string tenantId, int userId, int locationId)
    {
        var key = BuildAccessKey(tenantId, userId, locationId);
        return _accessCache.TryGetValue(key, out var access) ? access : null;
    }

    public Dictionary<int, ResolvedAccess?> GetAccessMap(string tenantId, int userId)
    {
        var prefix = $"{tenantId}:{userId}:";
        var map = new Dictionary<int, ResolvedAccess?>();

        foreach (var kvp in _accessCache)
        {
            if (kvp.Key.StartsWith(prefix))
            {
                var locationIdStr = kvp.Key.Substring(prefix.Length);
                if (int.TryParse(locationIdStr, out var locationId))
                    map[locationId] = kvp.Value;
            }
        }

        return map;
    }

    public void SetAccess(string tenantId, int userId, int locationId, ResolvedAccess? access)
    {
        _accessCache[BuildAccessKey(tenantId, userId, locationId)] = access;
    }

    public void ClearUser(string tenantId, int userId)
    {
        var prefix = $"{tenantId}:{userId}:";
        foreach (var key in _accessCache.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _accessCache.TryRemove(key, out _);
    }

    public int? GetDisplayConstraint(string tenantId, int displayId)
    {
        return _displayConstraintCache.TryGetValue($"{tenantId}:{displayId}", out var constraintId)
            ? constraintId : null;
    }

    public void SetDisplayConstraint(string tenantId, int displayId, int? constraintId)
    {
        _displayConstraintCache[$"{tenantId}:{displayId}"] = constraintId;
    }

    public void ClearDisplayConstraints(string tenantId)
    {
        var prefix = $"{tenantId}:";
        foreach (var key in _displayConstraintCache.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _displayConstraintCache.TryRemove(key, out _);
    }

    public void ClearAll()
    {
        _accessCache.Clear();
        _displayConstraintCache.Clear();
    }

    private static string BuildAccessKey(string tenantId, int userId, int locationId)
        => $"{tenantId}:{userId}:{locationId}";
}
