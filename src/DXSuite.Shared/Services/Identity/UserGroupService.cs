using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Shared.Services.Identity;

public class UserGroupService : IUserGroupService
{
    private readonly IIdentityDbContext _db;
    private readonly Services.LocationAccessBroadcastService? _accessBroadcast;
    private readonly ITenantContext? _tenantContext;

    public UserGroupService(
        IIdentityDbContext db,
        Services.LocationAccessBroadcastService? accessBroadcast = null,
        ITenantContext? tenantContext = null)
    {
        _db = db;
        _accessBroadcast = accessBroadcast;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Group membership feeds group-scoped location access rules, so live circuits must
    /// re-resolve access when it changes. Skipped in sub-scopes with an unpopulated
    /// tenant context (e.g. login-time group sync) — there is no live circuit to update
    /// for a user who is still logging in.
    /// </summary>
    private void NotifyLocationAccessMayHaveChanged(int userId)
    {
        if (_tenantContext?.IsPopulated == true)
            _accessBroadcast?.NotifyUser(_tenantContext.TenantId, userId);
    }

    public async Task<List<TblUserGroup>> GetAllGroupsAsync(string? groupType = null)
    {
        var query = _db.TblUserGroups
            .Include(g => g.Memberships)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(groupType))
            query = query.Where(g => g.GroupType == groupType);

        return await query.OrderBy(g => g.GroupName).ToListAsync();
    }

    public async Task<TblUserGroup?> GetGroupByIdAsync(int groupId)
    {
        return await _db.TblUserGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.UserGroupId == groupId);
    }

    public async Task<TblUserGroup> CreateGroupAsync(string groupName, string groupType, string? description, string? externalId, string editUser)
    {
        var group = new TblUserGroup
        {
            GroupName = groupName,
            GroupType = groupType,
            Description = description,
            ExternalId = externalId,
            IsActive = true,
            CreateDate = DateTime.UtcNow,
            LastEditUser = editUser
        };

        _db.TblUserGroups.Add(group);
        await _db.SaveChangesAsync();
        return group;
    }

    public async Task UpdateGroupAsync(int groupId, string groupName, string groupType, string? description, string? externalId, bool isActive, string editUser)
    {
        var group = await _db.TblUserGroups.FindAsync(groupId)
            ?? throw new InvalidOperationException($"User group {groupId} not found");

        group.GroupName = groupName;
        group.GroupType = groupType;
        group.Description = description;
        group.ExternalId = externalId;
        group.IsActive = isActive;
        group.LastEditDate = DateTime.UtcNow;
        group.LastEditUser = editUser;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteGroupAsync(int groupId)
    {
        var group = await _db.TblUserGroups.FindAsync(groupId);
        if (group != null)
        {
            _db.TblUserGroups.Remove(group);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<TblUser>> GetGroupMembersAsync(int groupId)
    {
        return await _db.TblUserGroupMemberships
            .AsNoTracking()
            .Where(m => m.UserGroupId == groupId)
            .Include(m => m.User)
            .Select(m => m.User)
            .OrderBy(u => u.FriendlyName)
            .ToListAsync();
    }

    public async Task AddMemberAsync(int groupId, int userId)
    {
        var exists = await _db.TblUserGroupMemberships
            .AnyAsync(m => m.UserGroupId == groupId && m.UserId == userId);

        if (!exists)
        {
            _db.TblUserGroupMemberships.Add(new TblUserGroupMembership
            {
                UserGroupId = groupId,
                UserId = userId,
                AddedDate = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            NotifyLocationAccessMayHaveChanged(userId);
        }
    }

    public async Task RemoveMemberAsync(int groupId, int userId)
    {
        var membership = await _db.TblUserGroupMemberships
            .FirstOrDefaultAsync(m => m.UserGroupId == groupId && m.UserId == userId);

        if (membership != null)
        {
            _db.TblUserGroupMemberships.Remove(membership);
            await _db.SaveChangesAsync();
            NotifyLocationAccessMayHaveChanged(userId);
        }
    }

    public async Task<List<TblUserGroup>> GetGroupsForUserAsync(int userId, string? groupType = null)
    {
        var query = _db.TblUserGroupMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Include(m => m.UserGroup)
            .Select(m => m.UserGroup);

        if (!string.IsNullOrEmpty(groupType))
            query = query.Where(g => g.GroupType == groupType);

        return await query.OrderBy(g => g.GroupName).ToListAsync();
    }
}
