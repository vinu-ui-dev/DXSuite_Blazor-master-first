namespace DXSuite.Shared.Services;

/// <summary>
/// Singleton event bus telling live circuits to re-resolve location access after an
/// access-changing write (access rules, group membership). Events carry the TenantId
/// because user ids are only unique within a single tenant's Director DB — subscribers
/// must match on both tenant and user before refreshing.
/// </summary>
public class LocationAccessBroadcastService
{
    public event Action<string, IReadOnlyCollection<int>>? OnLocationAccessChanged;

    public void NotifyUser(string tenantId, int userId) => NotifyUsers(tenantId, new[] { userId });

    public void NotifyUsers(string tenantId, IReadOnlyCollection<int> userIds)
    {
        if (userIds.Count > 0)
            OnLocationAccessChanged?.Invoke(tenantId, userIds);
    }
}
