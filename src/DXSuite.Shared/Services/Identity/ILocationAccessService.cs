using DXSuite.Shared.Authorization;

namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// Minimal location access service interface for cross-module use.
/// Full CRUD interface with entity types lives in Director.
/// </summary>
public interface ILocationAccessService
{
    Task<ResolvedAccess?> ResolveAccessAsync(int userId, int locationId);
    Task<ResolvedAccess?> ResolveAccessAsync(int userId, int locationId, DateTime asOf);
    Task<bool> HasAccessRulesAsync(int locationId);
    Task<List<int>> GetAllLocationIdsAsync();
    Task<List<int>> GetAccessibleLocationIdsAsync(int userId, List<int> userGroupIds);
}
