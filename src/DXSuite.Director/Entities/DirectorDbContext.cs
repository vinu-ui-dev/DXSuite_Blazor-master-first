using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

public class DirectorDbContext : DbContext, IDirectorDBContext, IIdentityDbContext
{
    public DirectorDbContext()
    {
    }

    public DirectorDbContext(DbContextOptions<DirectorDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<TblAirlineLocation> TblAirlineLocations { get; set; }

    public virtual DbSet<TblAsset> TblAssets { get; set; }

    public virtual DbSet<TblAssetGroup> TblAssetGroups { get; set; }

    public virtual DbSet<TblAssetType> TblAssetTypes { get; set; }

    public virtual DbSet<TblConstraint> TblConstraints { get; set; }

    public virtual DbSet<TblDevice> TblDevices { get; set; }

    public virtual DbSet<TblDeviceModelConfig> TblDeviceModelConfigs { get; set; }

    public virtual DbSet<TblDisplay> TblDisplays { get; set; }

    public virtual DbSet<TblSite> TblSites { get; set; }

    public virtual DbSet<TblDisplayGroup> TblDisplayGroups { get; set; }

    public virtual DbSet<TblDisplayType> TblDisplayTypes { get; set; }

    public virtual DbSet<TblEndpoint> TblEndpoints { get; set; }

    public virtual DbSet<TblEventLog> TblEventLogs { get; set; }

    public virtual DbSet<TblEventSeverity> TblEventSeverities { get; set; }

    public virtual DbSet<TblEventType> TblEventTypes { get; set; }

    public virtual DbSet<TblLocation> TblLocations { get; set; }

    public virtual DbSet<TblLocationGroup> TblLocationGroups { get; set; }

    public virtual DbSet<TblLocationType> TblLocationTypes { get; set; }

    public virtual DbSet<TblParameter> TblParameters { get; set; }

    public virtual DbSet<TblScheduleEvent> TblScheduleEvents { get; set; }

    public virtual DbSet<TblUser> TblUsers { get; set; }

    public virtual DbSet<TblUserLocation> TblUserLocations { get; set; }

    public virtual DbSet<TblUserRole> TblUserRoles { get; set; }

    public virtual DbSet<TblRolePermission> TblRolePermissions { get; set; }

    public virtual DbSet<TblKnownPermission> TblKnownPermissions { get; set; }

    public virtual DbSet<ViewActiveDisplayContent> ViewActiveDisplayContents { get; set; }

    public virtual DbSet<TblOverride> TblOverrides { get; set; }

    public virtual DbSet<TblOverrideDevice> TblOverrideDevices { get; set; }

    public virtual DbSet<TblOverrideContent> TblOverrideContents { get; set; }

    public virtual DbSet<TblOverrideDisplay> TblOverrideDisplays { get; set; }

    public virtual DbSet<TblContentHealthEvent> TblContentHealthEvents { get; set; }

    public virtual DbSet<TblDeviceType> TblDeviceTypes { get; set; }

    public virtual DbSet<TblApiKey> TblApiKeys { get; set; }

    public virtual DbSet<TblDeviceMetadata> TblDeviceMetadata { get; set; }

    public virtual DbSet<TblAssetApproval> TblAssetApprovals { get; set; }

    public virtual DbSet<TblUserGroup> TblUserGroups { get; set; }

    public virtual DbSet<TblUserGroupMembership> TblUserGroupMemberships { get; set; }

    public virtual DbSet<TblApprovalChain> TblApprovalChains { get; set; }

    public virtual DbSet<TblApprovalStage> TblApprovalStages { get; set; }

    public virtual DbSet<TblApprovalStageGroup> TblApprovalStageGroups { get; set; }

    public virtual DbSet<TblApprovalRequest> TblApprovalRequests { get; set; }

    public virtual DbSet<TblApprovalQueueEntry> TblApprovalQueueEntries { get; set; }

    public virtual DbSet<TblApprovalAction> TblApprovalActions { get; set; }

    public virtual DbSet<TblApprovalComment> TblApprovalComments { get; set; }

    public virtual DbSet<TblNotification> TblNotifications { get; set; }

    public virtual DbSet<TblSystemSetting> TblSystemSettings { get; set; }

    public virtual DbSet<TblUserMetadata> TblUserMetadata { get; set; }

    public virtual DbSet<TblUserGroupMetadata> TblUserGroupMetadata { get; set; }

    public virtual DbSet<TblLocationAccessRule> TblLocationAccessRules { get; set; }

    public virtual DbSet<TblLocationMetadataRule> TblLocationMetadataRules { get; set; }

    public virtual DbSet<TblAssetContentMetadata> TblAssetContentMetadata { get; set; }

    public virtual DbSet<TblDiscoveredAdGroup> TblDiscoveredAdGroups { get; set; }

    // Floor Plan System DbSets
    public virtual DbSet<TblFloorPlanElement> TblFloorPlanElements { get; set; }
    public virtual DbSet<TblFloorPlanIconSchedule> TblFloorPlanIconSchedules { get; set; }

    // Access Schedule DbSets
    public virtual DbSet<TblAccessSchedule> TblAccessSchedules { get; set; }

    // Engage (Control Client) DbSets
    public virtual DbSet<TblControlClient> TblControlClients { get; set; }
    public virtual DbSet<TblControlClientDisplay> TblControlClientDisplays { get; set; }
    public virtual DbSet<TblControlClientPairingRequest> TblControlClientPairingRequests { get; set; }
    public virtual DbSet<TblControlClientToken> TblControlClientTokens { get; set; }
    public virtual DbSet<TblContentManifestLog> TblContentManifestLogs { get; set; }
    public virtual DbSet<TblDeviceCommand> TblDeviceCommands { get; set; }
    public virtual DbSet<TblDevicePairingRequest> TblDevicePairingRequests { get; set; }
    public virtual DbSet<TblDeviceToken> TblDeviceTokens { get; set; }

    // LLM Configuration DbSets
    public virtual DbSet<TblLlmInteraction> TblLlmInteractions { get; set; }
    public virtual DbSet<TblLlmPromptConfiguration> TblLlmPromptConfigurations { get; set; }
    public virtual DbSet<TblLlmProviderConfiguration> TblLlmProviderConfigurations { get; set; }

    // Cloud Platform Connector DbSets
    public virtual DbSet<TblCloudPlatformConnector> TblCloudPlatformConnectors { get; set; }

    // Event Log Archive
    public virtual DbSet<TblEventLogArchive> TblEventLogArchives { get; set; }

    // Metadata Definition
    public virtual DbSet<TblMetadataDefinition> TblMetadataDefinitions { get; set; }

    // User Notification Preferences
    public virtual DbSet<TblUserNotificationPreference> TblUserNotificationPreferences { get; set; }

    // Local Accounts
    public virtual DbSet<TblLocalAccount> TblLocalAccounts { get; set; }

    // Video Wall Subsystem DbSets
    public virtual DbSet<TblUserGroupLocationDefault> TblUserGroupLocationDefaults { get; set; }
    public virtual DbSet<TblUserGroupLocationLogo> TblUserGroupLocationLogos { get; set; }
    public virtual DbSet<TblUserGroupLocationLayerContent> TblUserGroupLocationLayerContents { get; set; }
    public virtual DbSet<TblDirectorUrl> TblDirectorUrls { get; set; }
    public virtual DbSet<TblWallLayoutMapping> TblWallLayoutMappings { get; set; }

    // Content Package Subsystem DbSets
    public virtual DbSet<TblContentPackageHistory> TblContentPackageHistories { get; set; }
    public virtual DbSet<TblContentPackageHistoryItem> TblContentPackageHistoryItems { get; set; }
    public virtual DbSet<TblContentPackageEnvironmentLabel> TblContentPackageEnvironmentLabels { get; set; }

    // Content Channel DbSets
    public virtual DbSet<TblContentChannel> TblContentChannels { get; set; }
    public virtual DbSet<TblContentChannelMetadata> TblContentChannelMetadata { get; set; }

    // Location Bundle Subsystem DbSets
    public virtual DbSet<TblLocationBundle> TblLocationBundles { get; set; }
    public virtual DbSet<TblLocationBundleVersion> TblLocationBundleVersions { get; set; }
    public virtual DbSet<TblLocationBundleHistory> TblLocationBundleHistories { get; set; }
    public virtual DbSet<TblLocationBundleStorage> TblLocationBundleStorages { get; set; }

    // Panels
    public virtual DbSet<TblPanel> TblPanels { get; set; }

    // Auto-Update System DbSets
    public virtual DbSet<TblAppPlatform> TblAppPlatforms { get; set; }
    public virtual DbSet<TblAppVersion> TblAppVersions { get; set; }

    public virtual DbSet<TblFirmwareImage> TblFirmwareImages { get; set; }

    public virtual DbSet<TblProvisioningPackage> TblProvisioningPackages { get; set; }

    public virtual DbSet<TblProvisioningAuthorization> TblProvisioningAuthorizations { get; set; }
    public virtual DbSet<TblUpdateDeployment> TblUpdateDeployments { get; set; }
    public virtual DbSet<TblUpdateDeploymentDevice> TblUpdateDeploymentDevices { get; set; }
    public virtual DbSet<TblUpdateDeploymentProgress> TblUpdateDeploymentProgress { get; set; }

    // Messaging
    public virtual DbSet<TblMessageType> TblMessageTypes { get; set; }
    public virtual DbSet<TblMessage> TblMessages { get; set; }
    public virtual DbSet<TblMessageText> TblMessageTexts { get; set; }
    public virtual DbSet<TblMessageActivation> TblMessageActivations { get; set; }
    public virtual DbSet<TblMessageActivationTarget> TblMessageActivationTargets { get; set; }
    public virtual DbSet<TblMessagePermission> TblMessagePermissions { get; set; }
    public virtual DbSet<TblZone> TblZones { get; set; }
    public virtual DbSet<TblZoneDisplay> TblZoneDisplays { get; set; }

    public async Task<int> ExecuteSqlRawAsync(string sqlCommand)
    {
        return await ((DbContext)this).Database.ExecuteSqlRawAsync(sqlCommand);
    }

    public async Task<int> ExecuteSqlRawAsync(string sqlCommand, SqlParameter sqlParameter)
    {
        return await ((DbContext)this).Database.ExecuteSqlRawAsync(sqlCommand, sqlParameter);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return base.SaveChangesAsync();
    }

    public void MarkAsModified<T>(T entity)
    {
        if (entity != null)
        {
            Entry(entity).State = EntityState.Modified;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TblLocationBundle>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsEnabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<TblLocationBundleVersion>(entity =>
        {
            entity.Property(e => e.UploadDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.LocationBundle).WithMany(p => p.Versions)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblLocationBundleVersion_tblLocationBundle");
        });

        modelBuilder.Entity<TblLocationBundleStorage>(entity =>
        {
            entity.Property(e => e.LastEditDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Location).WithMany()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblLocationBundleStorage_tblLocation");
        });

        modelBuilder.Entity<TblPanel>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsEnabled).HasDefaultValue(true);

            // Restrict rather than cascade: deleting a location that still has a panel
            // pointed at it would silently break a live wall in a customer's room.
            entity.HasOne(d => d.Location).WithMany()
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblPanel_tblLocation");
        });

        modelBuilder.Entity<TblAirlineLocation>(entity =>
        {
            entity.HasKey(e => e.AirlineLocationId).HasName("PK_tblAirlineLocation_1");

            entity.HasOne(d => d.Location).WithMany(p => p.TblAirlineLocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAirlineLocation_tblLocation1");
        });

        modelBuilder.Entity<TblAsset>(entity =>
        {
            entity.Property(e => e.AssetHeight).HasDefaultValue(1080.0);
            entity.Property(e => e.AssetWidth).HasDefaultValue(1920.0);
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Duration).HasDefaultValue(7.0);
            entity.Property(e => e.SortOrder).HasDefaultValue(99);

            entity.HasOne(d => d.AssetGroup).WithMany(p => p.TblAssets)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAsset_tblAssetGroup");

            entity.HasOne(d => d.AssetType).WithMany(p => p.TblAssets)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAsset_tblAssetType");

            entity.HasOne(d => d.Constraint).WithMany(p => p.TblAssets)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAsset_tblConstraint");

            entity.HasOne(d => d.Location).WithMany(p => p.TblAssets)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAsset_tblLocation");
        });

        modelBuilder.Entity<TblAssetGroup>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblAssetType>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblConstraint>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblDevice>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Display).WithMany(p => p.TblDevices).HasConstraintName("FK_tblDevice_tblDisplay");

            entity.HasOne(d => d.Location).WithMany(p => p.TblDevices).HasConstraintName("FK_tblDevice_tblLocation");

            entity.HasOne(d => d.DeviceType).WithMany(p => p.TblDevices).HasConstraintName("FK_tblDevice_tblDeviceType");

            entity.HasIndex(e => e.SiteId, "IX_tblDevice_SiteId");

            entity.Property(e => e.Orientation).HasDefaultValue(Services.DeviceOrientation.Landscape);
            entity.Property(e => e.VideoPath).HasDefaultValue(Services.DeviceVideoPath.Hardware);

            // Deleting a site must never delete players, matching FK_tblDevice_TargetAppVersion.
            entity.HasOne(d => d.Site).WithMany(p => p.Devices)
                .HasForeignKey(d => d.SiteId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblDevice_tblSite");
        });

        modelBuilder.Entity<TblContentChannel>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getutcdate())");

            // One device can back at most one channel (sparse unique: only non-null DeviceIds are unique)
            entity.HasIndex(e => e.DeviceId).IsUnique()
                .HasFilter("[DeviceId] IS NOT NULL");

            entity.HasOne(e => e.Location).WithMany()
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Display).WithMany()
                .HasForeignKey(e => e.DisplayId)
                .OnDelete(DeleteBehavior.SetNull);

            // One-to-one with TblDevice — TblContentChannel holds the FK
            entity.HasOne(e => e.Device).WithOne()
                .HasForeignKey<TblContentChannel>(e => e.DeviceId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Metadata).WithOne(m => m.Channel)
                .HasForeignKey(m => m.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TblContentChannelMetadata>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getutcdate())");

            entity.HasIndex(e => new { e.ChannelId, e.MetadataKey }).IsUnique();
        });

        modelBuilder.Entity<TblDisplay>(entity =>
        {
            entity.HasKey(e => e.DisplayId).HasName("PK_tblDisplay_1");

            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.DisplayGroupId).HasDefaultValue(1);
            entity.Property(e => e.DisplayHeight).HasDefaultValue(1080.0);
            entity.Property(e => e.DisplayWidth).HasDefaultValue(1920.0);
            entity.Property(e => e.Enabled).HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasDefaultValue(99);

            entity.HasOne(d => d.Constraint).WithMany(p => p.TblDisplays)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblDisplay_tblConstraint");

            entity.HasOne(d => d.DisplayGroup).WithMany(p => p.TblDisplays)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblDisplay_tblDisplayGroup");

            entity.HasOne(d => d.DisplayType).WithMany(p => p.TblDisplays)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblDisplay_tblDisplayType");

            entity.HasOne(d => d.Endpoint).WithMany(p => p.TblDisplays).HasConstraintName("FK_tblDisplay_tblEndpoint");

            entity.HasOne(d => d.Location).WithMany(p => p.TblDisplays)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblDisplay_tblLocation");

            entity.HasOne(d => d.OverrideAsset).WithMany(p => p.TblDisplays).HasConstraintName("FK_tblDisplay_tblAsset");
        });

        modelBuilder.Entity<TblDisplayGroup>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.SortOrder).HasDefaultValue(99);
        });

        modelBuilder.Entity<TblDisplayType>(entity =>
        {
            entity.Property(e => e.DisplayTypeId).ValueGeneratedNever();
        });

        modelBuilder.Entity<TblEndpoint>(entity =>
        {
            entity.Property(e => e.DateCreated).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblEventLog>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Asset).WithMany(p => p.TblEventLogs).HasConstraintName("FK_tblEventLog_tblAsset");

            entity.HasOne(d => d.Display).WithMany(p => p.TblEventLogs).HasConstraintName("FK_tblEventLog_tblDisplay");

            entity.HasOne(d => d.EventSeverity).WithMany(p => p.TblEventLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblEventLog_tblEventSeverity");

            entity.HasOne(d => d.EventType).WithMany(p => p.TblEventLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblEventLog_tblEventType");

            entity.HasOne(d => d.ScheduleEvent).WithMany(p => p.TblEventLogs).HasConstraintName("FK_tblEventLog_tblScheduleEvent");
        });

        modelBuilder.Entity<TblEventSeverity>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblEventType>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblLocation>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.SortOrder).HasDefaultValue(99);

            entity.Property(e => e.IsTransientCluster).HasDefaultValue(false);
            entity.Property(e => e.TargetContentByMetadata).HasDefaultValue(false);
            entity.Property(e => e.MetadataMatchMode).HasDefaultValue("all");

            // Self-referencing FK: transient cluster locations point to their parent VideoWall.
            entity.HasOne(d => d.ParentVideoWallLocation).WithMany()
                .HasForeignKey(d => d.ParentVideoWallLocationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblLocation_ParentVideoWall");

            entity.HasOne(d => d.OwningUserGroup).WithMany()
                .HasForeignKey(d => d.OwningUserGroupId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblLocation_OwningUserGroup");

            entity.HasOne(d => d.LocationGroup).WithMany(p => p.TblLocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblLocation_tblLocationGroup");

            entity.HasOne(d => d.LocationType).WithMany(p => p.TblLocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblLocation_tblLocationType1");
        });

        modelBuilder.Entity<TblLocationGroup>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.SortOrder).HasDefaultValue(99);
        });

        modelBuilder.Entity<TblLocationType>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblParameter>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblScheduleEvent>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Friday).HasDefaultValue(true);
            entity.Property(e => e.Monday).HasDefaultValue(true);
            entity.Property(e => e.Saturday).HasDefaultValue(true);
            entity.Property(e => e.Sunday).HasDefaultValue(true);
            entity.Property(e => e.Thursday).HasDefaultValue(true);
            entity.Property(e => e.Tuesday).HasDefaultValue(true);
            entity.Property(e => e.Wednesday).HasDefaultValue(true);

            entity.HasOne(d => d.Display).WithMany(p => p.TblScheduleEvents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblScheduleEvent_tblDisplay");

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblScheduleEvent_UserGroup");

            entity.HasOne(d => d.OverrideConstraint).WithMany()
                .HasForeignKey(d => d.OverrideConstraintId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblScheduleEvent_OverrideConstraint");
        });

        modelBuilder.Entity<TblUser>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            // Concurrent first-login circuits can race the find-or-create in SessionState;
            // this index makes the database reject the duplicate. Filtered because the column
            // is nullable and SQL Server unique indexes would otherwise allow only one NULL row.
            // Mirrored in IdentityDbContext, which maps the same table.
            entity.HasIndex(e => e.UserPrincipalName)
                .IsUnique()
                .HasDatabaseName("UX_tblUser_UserPrincipalName")
                .HasFilter("[UserPrincipalName] IS NOT NULL");

            entity.HasOne(d => d.Role).WithMany(p => p.TblUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblUser_tblUserRole");
        });

        modelBuilder.Entity<TblUserLocation>(entity =>
        {
            entity.HasOne(d => d.Location).WithMany(p => p.TblUserLocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblUserLocation_tblLocation");

            entity.HasOne(d => d.User).WithMany()
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblUserLocation_tblUser");
        });

        modelBuilder.Entity<TblUserRole>(entity =>
        {
            entity.Property(e => e.IsGlobal).HasDefaultValue(true);
            entity.HasOne(d => d.BaseRole).WithMany()
                .HasForeignKey(d => d.BaseRoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblUserRole_BaseRole");
        });

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

        modelBuilder.Entity<TblKnownPermission>(entity =>
        {
            entity.Property(e => e.DiscoveredDate).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<ViewActiveDisplayContent>(entity =>
        {
            entity.ToView("View_ActiveDisplayContent");
        });

        // Override System Entities
        modelBuilder.Entity<TblOverride>(entity =>
        {
            entity.HasKey(e => e.OverrideId).HasName("PK_tblOverride");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ModifiedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Priority).HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasDefaultValue(false);
            entity.Property(e => e.IsScheduled).HasDefaultValue(false);
        });

        modelBuilder.Entity<TblOverrideDevice>(entity =>
        {
            entity.HasKey(e => e.OverrideDeviceId).HasName("PK_tblOverrideDevice");

            entity.HasOne(d => d.Override).WithMany(p => p.OverrideDevices)
                .HasForeignKey(d => d.OverrideId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblOverrideDevice_tblOverride");

            entity.HasOne(d => d.Device).WithMany(p => p.OverrideDevices)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblOverrideDevice_tblDevice");

            entity.HasIndex(e => new { e.OverrideId, e.DeviceId }).IsUnique();
        });

        modelBuilder.Entity<TblOverrideContent>(entity =>
        {
            entity.HasKey(e => e.OverrideContentId).HasName("PK_tblOverrideContent");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Duration).HasDefaultValue(7);

            entity.HasOne(d => d.Override).WithMany(p => p.OverrideContents)
                .HasForeignKey(d => d.OverrideId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblOverrideContent_tblOverride");

            entity.HasOne(d => d.Device).WithMany(p => p.OverrideContents)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblOverrideContent_tblDevice");

            entity.HasOne(d => d.Asset).WithMany(p => p.OverrideContents)
                .HasForeignKey(d => d.AssetId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblOverrideContent_tblAsset");
        });

        modelBuilder.Entity<TblOverrideDisplay>(entity =>
        {
            entity.HasKey(e => e.OverrideDisplayId).HasName("PK_tblOverrideDisplay");

            entity.HasOne(d => d.Override).WithMany(p => p.OverrideDisplays)
                .HasForeignKey(d => d.OverrideId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblOverrideDisplay_tblOverride");

            entity.HasOne(d => d.Display).WithMany(p => p.OverrideDisplays)
                .HasForeignKey(d => d.DisplayId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblOverrideDisplay_tblDisplay");

            entity.HasIndex(e => new { e.OverrideId, e.DisplayId }).IsUnique();
        });

        // Device Type Configuration
        modelBuilder.Entity<TblDeviceType>(entity =>
        {
            entity.HasKey(e => e.DeviceTypeId).HasName("PK_tblDeviceType");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        // Content Health Monitoring
        modelBuilder.Entity<TblContentHealthEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK_tblContentHealthEvent");
            entity.Property(e => e.ReceivedTime).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Device)
                .WithMany()
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ContentHealthEvent_Device");

            entity.HasIndex(e => e.DeviceId).HasDatabaseName("IX_ContentHealthEvent_DeviceId");
            entity.HasIndex(e => e.EventTime).HasDatabaseName("IX_ContentHealthEvent_EventTime").IsDescending();
        });

        // API Key Configuration
        modelBuilder.Entity<TblApiKey>(entity =>
        {
            entity.HasKey(e => e.ApiKeyId).HasName("PK_tblApiKey");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CallCount).HasDefaultValue(0L);

            entity.HasOne(d => d.Location).WithMany(p => p.TblApiKeys)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblApiKey_tblLocation");

            entity.HasOne(d => d.CreatedByUser).WithMany()
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblApiKey_tblUser");
        });

        // Device Metadata (key/value pairs for URL params)
        modelBuilder.Entity<TblDeviceMetadata>(entity =>
        {
            entity.HasKey(e => e.DeviceMetadataId).HasName("PK_tblDeviceMetadata");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Device).WithMany(p => p.DeviceMetadata)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblDeviceMetadata_tblDevice");

            // Unique constraint on DeviceId + MetadataKey
            entity.HasIndex(e => new { e.DeviceId, e.MetadataKey })
                .IsUnique()
                .HasDatabaseName("UQ_DeviceMetadata");
        });

        // Asset Approval (hotel content approval workflow)
        modelBuilder.Entity<TblAssetApproval>(entity =>
        {
            entity.HasKey(e => e.AssetApprovalId).HasName("PK_tblAssetApproval");
            entity.Property(e => e.SubmittedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ApprovalStatus).HasDefaultValue(0);

            entity.HasOne(d => d.Asset).WithOne(p => p.AssetApproval)
                .HasForeignKey<TblAssetApproval>(d => d.AssetId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_AssetApproval_Asset");

            entity.HasOne(d => d.UploadedByUser).WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AssetApproval_Uploader");

            entity.HasOne(d => d.ReviewedByUser).WithMany()
                .HasForeignKey(d => d.ReviewedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AssetApproval_Reviewer");

            entity.HasIndex(e => e.ApprovalStatus).HasDatabaseName("IX_AssetApproval_Status");
        });

        // User Groups
        modelBuilder.Entity<TblUserGroup>(entity =>
        {
            entity.HasKey(e => e.UserGroupId).HasName("PK_tblUserGroup");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.GroupType).HasDefaultValue("director");
        });

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

        // Location Access Rules
        modelBuilder.Entity<TblLocationAccessRule>(entity =>
        {
            entity.HasKey(e => e.LocationAccessRuleId).HasName("PK_tblLocationAccessRule");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.AccessLevel).HasDefaultValue("full");

            // NoAction to match the production schema: cascading here would give SQL Server
            // a second cascade path from tblLocation to tblApprovalChain (via the SetNull on
            // FK_ApprovalChain_AccessRule), which it rejects with error 1785 (same limitation
            // as the Requirement->Category FK in DXSuite.Design).
            entity.HasOne(d => d.Location).WithMany(p => p.LocationAccessRules)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccessRule_Location");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccessRule_User");

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccessRule_UserGroup");
        });

        // Asset ownership. NoAction like FK_ContentMetadata_User: SET NULL would silently hand
        // scoped content to full-access users only, which should take an explicit act.
        modelBuilder.Entity<TblAsset>(entity =>
        {
            entity.HasOne(d => d.UploadedByUser).WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Asset_UploadedByUser");

            entity.HasOne(d => d.UploadedByUserGroup).WithMany()
                .HasForeignKey(d => d.UploadedByUserGroupId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Asset_UploadedByUserGroup");
        });

        // Location Metadata Rules
        modelBuilder.Entity<TblLocationMetadataRule>(entity =>
        {
            entity.HasKey(e => e.LocationMetadataRuleId).HasName("PK_tblLocationMetadataRule");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.UntaggedContentPolicy).HasDefaultValue("all");
            entity.Property(e => e.UntaggedPlayerPolicy).HasDefaultValue("all");

            entity.HasOne(d => d.Location).WithMany(p => p.MetadataRules)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_LocationMetadataRule_Location");
        });

        // Asset Content Metadata (metadata-based content scoping)
        modelBuilder.Entity<TblAssetContentMetadata>(entity =>
        {
            entity.HasKey(e => e.AssetContentMetadataId).HasName("PK_tblAssetContentMetadata");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            // Every read filters on (AssetId, MetadataKey) and expects the values back.
            entity.HasIndex(e => new { e.AssetId, e.MetadataKey }, "IX_ContentMetadata_AssetKey")
                .IncludeProperties(e => e.MetadataValue);

            entity.HasOne(d => d.Asset).WithMany(p => p.ContentMetadata)
                .HasForeignKey(d => d.AssetId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ContentMetadata_Asset");

            entity.HasOne(d => d.SetByUser).WithMany()
                .HasForeignKey(d => d.SetByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ContentMetadata_User");
        });

        // Approval Chain Configuration (supports multiple chains per location)
        modelBuilder.Entity<TblApprovalChain>(entity =>
        {
            entity.HasKey(e => e.ApprovalChainId).HasName("PK_tblApprovalChain");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsDefault).HasDefaultValue(true);
            entity.Property(e => e.AutoApproveMembers).HasDefaultValue(false);

            entity.HasOne(d => d.Location).WithMany(p => p.ApprovalChains)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ApprovalChain_Location");

            entity.HasOne(d => d.LocationAccessRule).WithMany(p => p.ApprovalChains)
                .HasForeignKey(d => d.LocationAccessRuleId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_ApprovalChain_AccessRule");
        });

        modelBuilder.Entity<TblApprovalStage>(entity =>
        {
            entity.HasKey(e => e.ApprovalStageId).HasName("PK_tblApprovalStage");
            entity.Property(e => e.IntraStageOperator).HasDefaultValue("OR");

            entity.HasOne(d => d.ApprovalChain).WithMany(p => p.Stages)
                .HasForeignKey(d => d.ApprovalChainId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ApprovalStage_Chain");
        });

        modelBuilder.Entity<TblApprovalStageGroup>(entity =>
        {
            entity.HasKey(e => e.ApprovalStageGroupId).HasName("PK_tblApprovalStageGroup");

            entity.HasOne(d => d.ApprovalStage).WithMany(p => p.StageGroups)
                .HasForeignKey(d => d.ApprovalStageId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ApprovalStageGroup_Stage");

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalStageGroup_Group");
        });

        // Approval Chain Runtime
        modelBuilder.Entity<TblApprovalRequest>(entity =>
        {
            entity.HasKey(e => e.ApprovalRequestId).HasName("PK_tblApprovalRequest");
            entity.Property(e => e.SubmittedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.AssetApproval).WithMany(p => p.ApprovalRequests)
                .HasForeignKey(d => d.AssetApprovalId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ApprovalRequest_AssetApproval");

            entity.HasOne(d => d.ApprovalChain).WithMany()
                .HasForeignKey(d => d.ApprovalChainId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalRequest_Chain");

            entity.HasOne(d => d.CurrentStage).WithMany()
                .HasForeignKey(d => d.CurrentStageId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalRequest_CurrentStage");

            entity.HasIndex(e => e.Status).HasDatabaseName("IX_ApprovalRequest_Status");
        });

        modelBuilder.Entity<TblApprovalQueueEntry>(entity =>
        {
            entity.HasKey(e => e.QueueEntryId).HasName("PK_tblApprovalQueueEntry");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.ApprovalRequest).WithMany(p => p.QueueEntries)
                .HasForeignKey(d => d.ApprovalRequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_QueueEntry_Request");

            entity.HasOne(d => d.ApprovalStage).WithMany()
                .HasForeignKey(d => d.ApprovalStageId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_QueueEntry_Stage");

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_QueueEntry_Group");

            entity.HasOne(d => d.ResolvedByUser).WithMany()
                .HasForeignKey(d => d.ResolvedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_QueueEntry_ResolvedBy");

            entity.HasIndex(e => new { e.ApprovalRequestId, e.Status })
                .HasDatabaseName("IX_QueueEntry_RequestStatus");
        });

        modelBuilder.Entity<TblApprovalAction>(entity =>
        {
            entity.HasKey(e => e.ApprovalActionId).HasName("PK_tblApprovalAction");
            entity.Property(e => e.ActionDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.ApprovalRequest).WithMany(p => p.Actions)
                .HasForeignKey(d => d.ApprovalRequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ApprovalAction_Request");

            entity.HasOne(d => d.ApprovalStage).WithMany()
                .HasForeignKey(d => d.ApprovalStageId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalAction_Stage");

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalAction_Group");

            entity.HasOne(d => d.ActingUser).WithMany()
                .HasForeignKey(d => d.ActingUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalAction_User");
        });

        // Approval Comments (threaded discussion on approval workflow)
        modelBuilder.Entity<TblApprovalComment>(entity =>
        {
            entity.HasKey(e => e.ApprovalCommentId).HasName("PK_tblApprovalComment");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.ApprovalRequest).WithMany()
                .HasForeignKey(d => d.ApprovalRequestId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalComment_Request");

            entity.HasOne(d => d.AssetApproval).WithMany()
                .HasForeignKey(d => d.AssetApprovalId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ApprovalComment_AssetApproval");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ApprovalComment_User");

            entity.HasIndex(e => e.AssetApprovalId).HasDatabaseName("IX_ApprovalComment_AssetApprovalId");
        });

        // Notifications (in-app + email alerts)
        modelBuilder.Entity<TblNotification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK_tblNotification");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.Property(e => e.EmailSent).HasDefaultValue(false);

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Notification_User");

            entity.HasIndex(e => new { e.UserId, e.IsRead }).HasDatabaseName("IX_Notification_UserRead");
        });

        // System Settings (key-value runtime config, e.g. SMTP credentials)
        modelBuilder.Entity<TblSystemSetting>(entity =>
        {
            entity.HasKey(e => e.SettingKey).HasName("PK_tblSystemSetting");
            entity.Property(e => e.LastEditDate).HasDefaultValueSql("(getdate())");
        });

        // User Metadata (key/value pairs for access control filtering)
        modelBuilder.Entity<TblUserMetadata>(entity =>
        {
            entity.HasKey(e => e.UserMetadataId).HasName("PK_tblUserMetadata");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.User).WithMany(p => p.UserMetadata)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserMetadata_User");
        });

        // User Group Metadata (key/value pairs for access control filtering)
        modelBuilder.Entity<TblUserGroupMetadata>(entity =>
        {
            entity.HasKey(e => e.UserGroupMetadataId).HasName("PK_tblUserGroupMetadata");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.UserGroup).WithMany(p => p.GroupMetadata)
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserGroupMetadata_UserGroup");
        });

        // Discovered AD Groups (Azure AD group discovery for admin adoption)
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

        // Auto-Update System entity configuration

        // TblAppPlatform configuration
        modelBuilder.Entity<TblAppPlatform>(entity =>
        {
            entity.HasKey(e => e.PlatformId).HasName("PK_tblAppPlatform");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.HasIndex(e => e.PlatformName).IsUnique();
            entity.HasIndex(e => e.FolderName).IsUnique();
        });

        // TblAppVersion configuration
        modelBuilder.Entity<TblAppVersion>(entity =>
        {
            entity.HasKey(e => e.AppVersionId).HasName("PK_tblAppVersion");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ModifiedDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Platform).WithMany(p => p.AppVersions)
                .HasForeignKey(d => d.PlatformId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblAppVersion_Platform");

            entity.HasIndex(e => new { e.PlatformId, e.VersionNumber }).IsUnique();
        });

        // TblUpdateDeployment configuration
        modelBuilder.Entity<TblUpdateDeployment>(entity =>
        {
            entity.HasKey(e => e.DeploymentId).HasName("PK_tblUpdateDeployment");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ModifiedDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.EnableBatching).HasDefaultValue(false);
            entity.Property(e => e.BatchSize).HasDefaultValue(10);
            entity.Property(e => e.CurrentBatchNumber).HasDefaultValue(0);
            entity.Property(e => e.TotalBatches).HasDefaultValue(0);

            entity.HasOne(d => d.AppVersion).WithMany(p => p.Deployments)
                .HasForeignKey(d => d.AppVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblUpdateDeployment_AppVersion");

            entity.HasOne(d => d.Location).WithMany(p => p.UpdateDeployments)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblUpdateDeployment_Location");
        });

        // TblUpdateDeploymentDevice configuration
        modelBuilder.Entity<TblUpdateDeploymentDevice>(entity =>
        {
            entity.HasKey(e => e.DeploymentDeviceId).HasName("PK_tblUpdateDeploymentDevice");

            entity.HasOne(d => d.Deployment).WithMany(p => p.DeploymentDevices)
                .HasForeignKey(d => d.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblUpdateDeploymentDevice_Deployment");

            entity.HasOne(d => d.Device).WithMany(p => p.UpdateDeploymentDevices)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblUpdateDeploymentDevice_Device");

            entity.HasIndex(e => new { e.DeploymentId, e.DeviceId }).IsUnique();
        });

        // TblUpdateDeploymentProgress configuration
        modelBuilder.Entity<TblUpdateDeploymentProgress>(entity =>
        {
            entity.HasKey(e => e.ProgressId).HasName("PK_tblUpdateDeploymentProgress");

            entity.HasOne(d => d.Deployment).WithMany(p => p.DeploymentProgress)
                .HasForeignKey(d => d.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblUpdateDeploymentProgress_Deployment");

            entity.HasOne(d => d.Device).WithMany(p => p.UpdateDeploymentProgress)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblUpdateDeploymentProgress_Device");

            entity.HasIndex(e => new { e.DeploymentId, e.DeviceId }).IsUnique();
            entity.HasIndex(e => new { e.DeploymentId, e.BatchNumber });
        });

        // Floor Plan Elements
        modelBuilder.Entity<TblFloorPlanElement>(entity =>
        {
            entity.HasKey(e => e.FloorPlanElementId).HasName("PK_tblFloorPlanElement");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Location).WithMany(p => p.FloorPlanElements)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FloorPlanElement_Location");

            entity.HasOne(d => d.ChildLocation).WithMany()
                .HasForeignKey(d => d.ChildLocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_FloorPlanElement_ChildLocation");

            entity.HasOne(d => d.ChildDisplay).WithMany()
                .HasForeignKey(d => d.ChildDisplayId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_FloorPlanElement_ChildDisplay");
        });

        // Floor Plan Icon Schedules
        modelBuilder.Entity<TblFloorPlanIconSchedule>(entity =>
        {
            entity.HasKey(e => e.FloorPlanIconScheduleId).HasName("PK_tblFloorPlanIconSchedule");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.FloorPlanElement).WithMany(p => p.IconSchedules)
                .HasForeignKey(d => d.FloorPlanElementId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FloorPlanIconSchedule_Element");

            entity.HasOne(d => d.IconAsset).WithMany()
                .HasForeignKey(d => d.IconAssetId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_FloorPlanIconSchedule_Asset");

            entity.HasOne(d => d.AssetLibraryLocation).WithMany()
                .HasForeignKey(d => d.AssetLibraryLocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_FloorPlanIconSchedule_AssetLibrary");
        });

        // Access Schedules (time windows on access rules)
        modelBuilder.Entity<TblAccessSchedule>(entity =>
        {
            entity.HasKey(e => e.AccessScheduleId).HasName("PK_tblAccessSchedule");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.LocationAccessRule).WithMany(p => p.AccessSchedules)
                .HasForeignKey(d => d.LocationAccessRuleId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_AccessSchedule_Rule");

            entity.HasOne(d => d.Display).WithMany()
                .HasForeignKey(d => d.DisplayId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccessSchedule_Display");

            entity.HasOne(d => d.Constraint).WithMany()
                .HasForeignKey(d => d.ConstraintId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccessSchedule_Constraint");
        });

        // Control Client System
        modelBuilder.Entity<TblControlClient>(entity =>
        {
            entity.HasKey(e => e.ControlClientId).HasName("PK_tblControlClient");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Location).WithMany(p => p.ControlClients)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ControlClient_Location");

            entity.HasOne(d => d.CreatedByUser).WithMany()
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_ControlClient_User");
        });

        modelBuilder.Entity<TblControlClientDisplay>(entity =>
        {
            entity.HasKey(e => e.ControlClientDisplayId).HasName("PK_tblControlClientDisplay");

            entity.HasOne(d => d.ControlClient).WithMany(p => p.ControlClientDisplays)
                .HasForeignKey(d => d.ControlClientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ControlClientDisplay_Client");

            entity.HasOne(d => d.Display).WithMany()
                .HasForeignKey(d => d.DisplayId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ControlClientDisplay_Display");
        });

        modelBuilder.Entity<TblControlClientPairingRequest>(entity =>
        {
            entity.HasKey(e => e.PairingRequestId).HasName("PK_tblControlClientPairingRequest");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.ControlClient).WithMany()
                .HasForeignKey(d => d.ControlClientId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_CCPairingRequest_Client");

            entity.HasOne(d => d.ResolvedByUser).WithMany()
                .HasForeignKey(d => d.ResolvedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_CCPairingRequest_ResolvedBy");
        });

        modelBuilder.Entity<TblControlClientToken>(entity =>
        {
            entity.HasKey(e => e.ControlClientTokenId).HasName("PK_tblControlClientToken");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.ControlClient).WithMany(p => p.ControlClientTokens)
                .HasForeignKey(d => d.ControlClientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ControlClientToken_Client");
        });

        modelBuilder.Entity<TblDevicePairingRequest>(entity =>
        {
            entity.HasKey(e => e.PairingRequestId).HasName("PK_tblDevicePairingRequest");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.ResolvedByUser).WithMany()
                .HasForeignKey(d => d.ResolvedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_DevicePairingRequest_ResolvedBy");
        });

        modelBuilder.Entity<TblDeviceToken>(entity =>
        {
            entity.HasKey(e => e.DeviceTokenId).HasName("PK_tblDeviceToken");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Device).WithMany()
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_DeviceToken_Device");

            entity.HasOne(d => d.ApprovedByUser).WithMany()
                .HasForeignKey(d => d.ApprovedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_DeviceToken_ApprovedBy");
        });

        // LLM Configuration
        modelBuilder.Entity<TblLlmInteraction>(entity =>
        {
            entity.HasKey(e => e.InteractionId).HasName("PK_tblLlmInteraction");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblLlmPromptConfiguration>(entity =>
        {
            entity.HasKey(e => e.PromptId).HasName("PK_tblLlmPromptConfig");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsEnabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<TblLlmProviderConfiguration>(entity =>
        {
            entity.HasKey(e => e.ConfigId).HasName("PK_tblLlmProviderConfig");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsEnabled).HasDefaultValue(true);
        });

        // Cloud Platform Connector
        modelBuilder.Entity<TblCloudPlatformConnector>(entity =>
        {
            entity.HasKey(e => e.ConnectorId).HasName("PK_tblCloudPlatformConnector");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.GatewayMode).HasDefaultValue("none");
            entity.Property(e => e.ContentMode).HasDefaultValue("monitoring-only");
        });

        // Event Log Archive
        modelBuilder.Entity<TblEventLogArchive>(entity =>
        {
            entity.HasKey(e => e.ArchiveId).HasName("PK_tblEventLogArchive");
        });

        // Metadata Definition
        modelBuilder.Entity<TblMetadataDefinition>(entity =>
        {
            entity.HasKey(e => e.MetadataDefinitionId).HasName("PK_tblMetadataDefinition");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            // A UNIQUE constraint rather than a unique index, matching what deployed databases carry.
            // Metadata keys drive playback, so two definitions for one key must be impossible.
            entity.HasAlternateKey(e => e.MetadataKey).HasName("UQ_MetadataDefinition_Key");
        });

        // User Notification Preferences
        modelBuilder.Entity<TblUserNotificationPreference>(entity =>
        {
            entity.HasKey(e => e.UserNotificationPreferenceId).HasName("PK_tblUserNotificationPreference");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_NotificationPreference_User");
        });

        // TblDevice - Add FK configurations for auto-update columns
        modelBuilder.Entity<TblDevice>(entity =>
        {
            entity.HasOne(d => d.TargetAppVersion).WithMany(p => p.TargetedDevices)
                .HasForeignKey(d => d.TargetAppVersionId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblDevice_TargetAppVersion");

            entity.HasOne(d => d.Platform).WithMany(p => p.Devices)
                .HasForeignKey(d => d.PlatformId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_tblDevice_Platform");
        });

        // Content Package subsystem: cross-environment stable identifiers.
        // Each in-scope entity gets a ContentPackageId (uniqueidentifier NOT NULL DEFAULT NEWID())
        // with a unique index, so bundles/imports can upsert records by ContentPackageId across
        // Dev/Pre-Prod/Prod environments regardless of local int PK values.
        modelBuilder.Entity<TblLocation>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblLocation_ContentPackageId");
        });
        modelBuilder.Entity<TblLocationGroup>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblLocationGroup_ContentPackageId");
        });
        modelBuilder.Entity<TblDisplay>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblDisplay_ContentPackageId");
        });
        modelBuilder.Entity<TblDisplayGroup>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblDisplayGroup_ContentPackageId");
        });
        modelBuilder.Entity<TblAsset>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblAsset_ContentPackageId");
        });
        modelBuilder.Entity<TblAssetGroup>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblAssetGroup_ContentPackageId");
        });
        modelBuilder.Entity<TblScheduleEvent>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblScheduleEvent_ContentPackageId");
        });
        modelBuilder.Entity<TblConstraint>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblConstraint_ContentPackageId");
        });
        modelBuilder.Entity<TblAccessSchedule>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblAccessSchedule_ContentPackageId");
        });
        modelBuilder.Entity<TblLocationAccessRule>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblLocationAccessRule_ContentPackageId");
        });
        modelBuilder.Entity<TblFloorPlanElement>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblFloorPlanElement_ContentPackageId");
        });
        modelBuilder.Entity<TblApprovalChain>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblApprovalChain_ContentPackageId");
        });
        modelBuilder.Entity<TblLocationMetadataRule>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblLocationMetadataRule_ContentPackageId");
        });
        modelBuilder.Entity<TblAssetContentMetadata>(entity =>
        {
            entity.Property(e => e.ContentPackageId).HasDefaultValueSql("newid()");
            entity.HasIndex(e => e.ContentPackageId).IsUnique().HasDatabaseName("UX_tblAssetContentMetadata_ContentPackageId");
        });

        // Video Wall — platform-agnostic layout mapping
        modelBuilder.Entity<TblWallLayoutMapping>(entity =>
        {
            entity.HasKey(e => e.WallLayoutMappingId).HasName("PK_tblWallLayoutMapping");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.LastEditDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.LayerRole).HasDefaultValue("status");

            entity.HasOne(d => d.Endpoint).WithMany()
                .HasForeignKey(d => d.EndpointId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblWallLayoutMapping_Endpoint");

            entity.HasOne(d => d.Display).WithMany()
                .HasForeignKey(d => d.DisplayId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblWallLayoutMapping_Display");
        });

        // Video Wall — airline default asset libraries per wall location
        modelBuilder.Entity<TblUserGroupLocationDefault>(entity =>
        {
            entity.HasKey(e => e.UserGroupLocationDefaultId).HasName("PK_tblUserGroupLocationDefault");
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.LastEditDate).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.BackgroundMode).HasDefaultValue("fill-width");

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblUserGroupLocationDefault_UserGroup");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblUserGroupLocationDefault_Location");

            entity.Property(e => e.MaskMode).HasDefaultValue("fill-width");

            entity.HasOne(d => d.DefaultConstraint).WithMany()
                .HasForeignKey(d => d.DefaultConstraintId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false)
                .HasConstraintName("FK_tblUserGroupLocationDefault_Constraint");
        });

        modelBuilder.Entity<TblMessageType>(entity =>
        {
            entity.HasData(
                new TblMessageType { MessageTypeId = 1, Code = "EMERGENCY", DisplayName = "Emergency", Priority = 10, DisplayMode = MessageDisplayMode.FullScreen, AllowsSignVideo = true, DefaultDisplaySeconds = 10, DefaultHideSeconds = 30 },
                new TblMessageType { MessageTypeId = 2, Code = "IRROPS", DisplayName = "IRROPS", Priority = 20, DisplayMode = MessageDisplayMode.Overlay, AllowsSignVideo = false, DefaultDisplaySeconds = 10, DefaultHideSeconds = 30 },
                new TblMessageType { MessageTypeId = 3, Code = "VISUAL_PAGING", DisplayName = "Visual Paging", Priority = 40, DisplayMode = MessageDisplayMode.Overlay, AllowsSignVideo = true, DefaultDisplaySeconds = 10, DefaultHideSeconds = 30 },
                new TblMessageType { MessageTypeId = 4, Code = "COUNTER", DisplayName = "Counter", Priority = 50, DisplayMode = MessageDisplayMode.Ticker, AllowsSignVideo = true, DefaultDisplaySeconds = 10, DefaultHideSeconds = 30 });
        });

        modelBuilder.Entity<TblMessage>(entity =>
        {
            entity.Property(e => e.CreatedUtc).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.MessageType).WithMany(p => p.Messages)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessage_tblMessageType");

            entity.HasOne(d => d.OwningUserGroup).WithMany()
                .HasForeignKey(d => d.OwningUserGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessage_OwningUserGroup");

            entity.HasOne(d => d.SignVideoAsset).WithMany()
                .HasForeignKey(d => d.SignVideoAssetId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessage_SignVideoAsset");
        });

        modelBuilder.Entity<TblMessageText>(entity =>
        {
            entity.HasOne(d => d.Message).WithMany(p => p.Texts)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblMessageText_tblMessage");
        });

        modelBuilder.Entity<TblMessageActivation>(entity =>
        {
            entity.Property(e => e.ActivatedUtc).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Message).WithMany(p => p.Activations)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessageActivation_tblMessage");
        });

        modelBuilder.Entity<TblMessageActivationTarget>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_tblMessageActivationTarget_OneTarget",
                "(CASE WHEN [ZoneId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DisplayId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LocationId] IS NULL THEN 0 ELSE 1 END) = 1"));

            entity.HasOne(d => d.MessageActivation).WithMany(p => p.Targets)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblMessageActivationTarget_tblMessageActivation");

            entity.HasOne(d => d.Zone).WithMany()
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessageActivationTarget_tblZone");

            entity.HasOne(d => d.Display).WithMany()
                .HasForeignKey(d => d.DisplayId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessageActivationTarget_tblDisplay");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_tblMessageActivationTarget_tblLocation");
        });

        modelBuilder.Entity<TblMessagePermission>(entity =>
        {
            entity.HasIndex(e => new { e.UserGroupId, e.MessageTypeId, e.ZoneId })
                .HasDatabaseName("UX_tblMessagePermission_Group_Type_Zone")
                .IsUnique()
                .HasFilter(null);

            entity.HasOne(d => d.UserGroup).WithMany()
                .HasForeignKey(d => d.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblMessagePermission_UserGroup");

            entity.HasOne(d => d.MessageType).WithMany()
                .HasForeignKey(d => d.MessageTypeId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblMessagePermission_tblMessageType");

            entity.HasOne(d => d.Zone).WithMany()
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblMessagePermission_tblZone");
        });

        modelBuilder.Entity<TblZone>(entity =>
        {
            entity.Property(e => e.CreatedUtc).HasDefaultValueSql("(sysutcdatetime())");
        });

        modelBuilder.Entity<TblZoneDisplay>(entity =>
        {
            entity.HasOne(d => d.Zone).WithMany(p => p.ZoneDisplays)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblZoneDisplay_tblZone");

            entity.HasOne(d => d.Display).WithMany()
                .HasForeignKey(d => d.DisplayId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tblZoneDisplay_tblDisplay");
        });
    }
}
