using Microsoft.Graph;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity
{
    public interface IAdGroupSyncService
    {
        Task SyncMembershipAsync(int userId, IReadOnlyList<string> tokenGroupOids);
        Task DiscoverGroupsAsync(IReadOnlyList<string> tokenGroupOids, GraphServiceClient? graphClient);
        Task<List<TblDiscoveredAdGroup>> GetUndiscoveredGroupsAsync();
        Task AdoptGroupAsync(int discoveredGroupId, string editUser);
        Task DismissGroupAsync(int discoveredGroupId);
        Task UndismissGroupAsync(int discoveredGroupId);
    }
}
