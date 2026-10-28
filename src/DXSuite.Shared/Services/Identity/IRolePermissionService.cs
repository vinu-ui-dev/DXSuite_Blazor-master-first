using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// Result of a role delete attempt. Deletion is refused (Deleted = false) while any
/// users are still assigned to the role; AssignedUserCount tells the UI how many.
/// </summary>
public record RoleDeleteResult(bool Deleted, int AssignedUserCount);

/// <summary>
/// CRUD and permission resolution for roles. The Admin role (RoleId 1) is locked:
/// it rejects all writes and implicitly holds every permission. Built-in (IsSystem)
/// roles accept permission updates but cannot be renamed, re-based, or deleted.
/// </summary>
public interface IRolePermissionService
{
    Task<List<TblUserRole>> GetAllRolesAsync();
    Task<TblUserRole?> GetRoleAsync(int roleId);
    Task<Dictionary<int, int>> GetAssignedUserCountsAsync();
    Task<int> GetAssignedUserCountAsync(int roleId);

    Task<TblUserRole> CreateRoleAsync(string roleName, string? description, int baseRoleId, IEnumerable<string> permissionKeys);
    Task UpdateRoleAsync(int roleId, string roleName, string? description, int? baseRoleId, IEnumerable<string> permissionKeys);
    Task<RoleDeleteResult> DeleteRoleAsync(int roleId);

    /// <summary>
    /// The effective permission set for a role. For Admin this is every known key.
    /// Results are cached (see RolePermissionCache); the first call per tenant also
    /// runs the default-grant sync for newly discovered permission keys.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsForRoleAsync(int roleId);

    /// <summary>
    /// True when the role holds the permission. Admin (RoleId 1) always returns true.
    /// </summary>
    Task<bool> HasPermissionAsync(int roleId, string permissionKey);

    /// <summary>
    /// Resolves the built-in role whose behavior legacy RoleEnum checks should use:
    /// built-in role ids map to themselves; custom roles map to their BaseRoleId.
    /// Falls back to NewUser (least privilege) when the base role is missing or invalid.
    /// </summary>
    Task<Roles.Role> GetEffectiveRoleAsync(int roleId);
}
