using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public interface IUserGroupService
{
    Task<List<TblUserGroup>> GetAllGroupsAsync(string? groupType = null);
    Task<TblUserGroup?> GetGroupByIdAsync(int groupId);
    Task<TblUserGroup> CreateGroupAsync(string groupName, string groupType, string? description, string? externalId, string editUser);
    Task UpdateGroupAsync(int groupId, string groupName, string groupType, string? description, string? externalId, bool isActive, string editUser);
    Task DeleteGroupAsync(int groupId);
    Task<List<TblUser>> GetGroupMembersAsync(int groupId);
    Task AddMemberAsync(int groupId, int userId);
    Task RemoveMemberAsync(int groupId, int userId);
    Task<List<TblUserGroup>> GetGroupsForUserAsync(int userId, string? groupType = null);
}
