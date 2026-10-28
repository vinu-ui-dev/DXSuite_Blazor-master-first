using System.Collections.Concurrent;

namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// Process-wide cache of resolved permission sets, keyed by (tenant, role).
/// Entries expire after a short TTL and are invalidated eagerly on every role save,
/// so permission edits reach live circuits on the same instance immediately and
/// other instances within the TTL.
/// </summary>
public class RolePermissionCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<(string TenantId, int RoleId), (IReadOnlySet<string> Permissions, DateTime LoadedUtc)> _entries = new();
    private readonly ConcurrentDictionary<string, bool> _syncedTenants = new();

    /// <summary>
    /// Serializes the per-tenant first-use default-grant sync so concurrent circuits
    /// don't race to insert the same known-permission rows.
    /// </summary>
    public SemaphoreSlim SyncLock { get; } = new(1, 1);

    public bool TryGet(string tenantId, int roleId, out IReadOnlySet<string> permissions)
    {
        if (_entries.TryGetValue((tenantId, roleId), out var entry) && DateTime.UtcNow - entry.LoadedUtc < Ttl)
        {
            permissions = entry.Permissions;
            return true;
        }

        permissions = null!;
        return false;
    }

    public void Set(string tenantId, int roleId, IReadOnlySet<string> permissions)
        => _entries[(tenantId, roleId)] = (permissions, DateTime.UtcNow);

    public bool IsSynced(string tenantId) => _syncedTenants.ContainsKey(tenantId);

    public void MarkSynced(string tenantId) => _syncedTenants[tenantId] = true;

    public void Invalidate(string tenantId)
    {
        foreach (var key in _entries.Keys.Where(k => k.TenantId == tenantId))
            _entries.TryRemove(key, out _);
    }
}
