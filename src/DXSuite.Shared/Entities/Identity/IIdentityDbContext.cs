using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

public interface IIdentityDbContext
{
    DbSet<TblUser> TblUsers { get; }
    DbSet<TblUserRole> TblUserRoles { get; }
    DbSet<TblRolePermission> TblRolePermissions { get; }
    DbSet<TblKnownPermission> TblKnownPermissions { get; }
    DbSet<TblUserGroup> TblUserGroups { get; }
    DbSet<TblUserGroupMembership> TblUserGroupMemberships { get; }
    DbSet<TblUserMetadata> TblUserMetadata { get; }
    DbSet<TblUserGroupMetadata> TblUserGroupMetadata { get; }
    DbSet<TblDiscoveredAdGroup> TblDiscoveredAdGroups { get; }
    DbSet<TblUserNotificationPreference> TblUserNotificationPreferences { get; }
    DbSet<TblMetadataDefinition> TblMetadataDefinitions { get; }
    DbSet<TblLocalAccount> TblLocalAccounts { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
