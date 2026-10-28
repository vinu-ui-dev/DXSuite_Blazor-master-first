using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Lightweight DbContext for identity tables only.
/// Used in monitoring-only deployments where the Director module is not loaded.
/// Connects to the same physical database as DirectorDbContext.
/// </summary>
public class IdentityDbContext : DbContext, IIdentityDbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<TblUser> TblUsers { get; set; } = null!;
    public DbSet<TblUserRole> TblUserRoles { get; set; } = null!;
    public DbSet<TblRolePermission> TblRolePermissions { get; set; } = null!;
    public DbSet<TblKnownPermission> TblKnownPermissions { get; set; } = null!;
    public DbSet<TblUserGroup> TblUserGroups { get; set; } = null!;
    public DbSet<TblUserGroupMembership> TblUserGroupMemberships { get; set; } = null!;
    public DbSet<TblUserMetadata> TblUserMetadata { get; set; } = null!;
    public DbSet<TblUserGroupMetadata> TblUserGroupMetadata { get; set; } = null!;
    public DbSet<TblDiscoveredAdGroup> TblDiscoveredAdGroups { get; set; } = null!;
    public DbSet<TblUserNotificationPreference> TblUserNotificationPreferences { get; set; } = null!;
    public DbSet<TblMetadataDefinition> TblMetadataDefinitions { get; set; } = null!;
    public DbSet<TblLocalAccount> TblLocalAccounts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TblUser
        modelBuilder.Entity<TblUser>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            // Concurrent first-login circuits can race the find-or-create in SessionState;
            // this index makes the database reject the duplicate. Filtered because the column
            // is nullable and SQL Server unique indexes would otherwise allow only one NULL row.
            entity.HasIndex(e => e.UserPrincipalName)
                .IsUnique()
                .HasDatabaseName("UX_tblUser_UserPrincipalName")
                .HasFilter("[UserPrincipalName] IS NOT NULL");
            entity.HasOne(d => d.Role).WithMany(p => p.TblUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblUser_tblUserRole");
        });

        // TblUserRole
        modelBuilder.Entity<TblUserRole>(entity =>
        {
            entity.Property(e => e.IsGlobal).HasDefaultValue(true);
            entity.HasOne(d => d.BaseRole).WithMany()
                .HasForeignKey(d => d.BaseRoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblUserRole_BaseRole");
        });

        // TblRolePermission
        modelBuilder.Entity<TblRolePermission>(entity =>
        {
            entity.HasKey(e => e.RolePermissionId).HasName("PK_tblRolePermission");
            entity.HasOne(d => d.Role).WithMany(p => p.Permissions)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RolePermission_UserRole");
            entity.HasIndex(e => new { e.RoleId, e.PermissionKey })
                .IsUnique()
                .HasDatabaseName("UQ_RolePermission");
        });

        // TblKnownPermission
        modelBuilder.Entity<TblKnownPermission>(entity =>
        {
            entity.Property(e => e.DiscoveredDate).HasDefaultValueSql("(getutcdate())");
        });

        // TblUserGroup
        modelBuilder.Entity<TblUserGroup>(entity =>
        {
            entity.HasKey(e => e.UserGroupId).HasName("PK_tblUserGroup");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.GroupType).HasDefaultValue("director");
        });

        // TblUserGroupMembership
        modelBuilder.Entity<TblUserGroupMembership>(entity =>
        {
            entity.HasKey(e => e.MembershipId).HasName("PK_tblUserGroupMembership");
            entity.Property(e => e.AddedDate).HasDefaultValueSql("(getdate())");
            entity.HasOne(d => d.UserGroup).WithMany(p => p.Memberships)
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserGroupMembership_UserGroup");
            entity.HasOne(d => d.User).WithMany(p => p.UserGroupMemberships)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserGroupMembership_User");
        });

        // TblUserMetadata
        modelBuilder.Entity<TblUserMetadata>(entity =>
        {
            entity.HasKey(e => e.UserMetadataId).HasName("PK_tblUserMetadata");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.HasOne(d => d.User).WithMany(p => p.UserMetadata)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserMetadata_User");
        });

        // TblUserGroupMetadata
        modelBuilder.Entity<TblUserGroupMetadata>(entity =>
        {
            entity.HasKey(e => e.UserGroupMetadataId).HasName("PK_tblUserGroupMetadata");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.HasOne(d => d.UserGroup).WithMany(p => p.GroupMetadata)
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserGroupMetadata_UserGroup");
        });


        // TblDiscoveredAdGroup
        modelBuilder.Entity<TblDiscoveredAdGroup>(entity =>
        {
            entity.HasKey(e => e.DiscoveredAdGroupId).HasName("PK_tblDiscoveredAdGroup");
            entity.Property(e => e.FirstSeenDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.LastSeenDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsDismissed).HasDefaultValue(false);
            entity.Property(e => e.IsAdopted).HasDefaultValue(false);
            entity.HasOne(d => d.AdoptedUserGroup).WithMany()
                .HasForeignKey(d => d.AdoptedUserGroupId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_DiscoveredAdGroup_UserGroup");
        });

        // TblMetadataDefinition
        modelBuilder.Entity<TblMetadataDefinition>(entity =>
        {
            entity.HasKey(e => e.MetadataDefinitionId).HasName("PK_tblMetadataDefinition");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            // A UNIQUE constraint rather than a unique index, matching what deployed databases carry.
            // Metadata keys drive playback, so two definitions for one key must be impossible.
            entity.HasAlternateKey(e => e.MetadataKey).HasName("UQ_MetadataDefinition_Key");
        });

        // TblUserNotificationPreference
        modelBuilder.Entity<TblUserNotificationPreference>(entity =>
        {
            entity.HasKey(e => e.UserNotificationPreferenceId).HasName("PK_tblUserNotificationPreference");
            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_NotificationPreference_User");
        });

        // TblLocalAccount
        modelBuilder.Entity<TblLocalAccount>(entity =>
        {
            entity.HasKey(e => e.LocalAccountId).HasName("PK_tblLocalAccount");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_LocalAccount_User");
        });
    }
}
