using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities
{
    public interface IDirectorDBContext
    {
        DbSet<TblAsset> TblAssets { get; set; }

        DbSet<TblAssetGroup> TblAssetGroups { get; set; }

        DbSet<TblAssetType> TblAssetTypes { get; set; }

        DbSet<TblConstraint> TblConstraints { get; set; }

        DbSet<TblDisplay> TblDisplays { get; set; }

        DbSet<TblSite> TblSites { get; set; }

        DbSet<TblEventLog> TblEventLogs { get; set; }

        DbSet<TblEventSeverity> TblEventSeverities { get; set; }

        DbSet<TblEventType> TblEventTypes { get; set; }

        DbSet<TblLocation> TblLocations { get; set; }

        DbSet<TblLocationGroup> TblLocationGroups { get; set; }

        DbSet<TblLocationType> TblLocationTypes { get; set; }

        DbSet<TblScheduleEvent> TblScheduleEvents { get; set; }

        DbSet<TblAirlineLocation> TblAirlineLocations { get; set; }

        DbSet<TblUserLocation> TblUserLocations { get; set; }

        DbSet<TblUser> TblUsers { get; set; }

        DbSet<TblEndpoint> TblEndpoints { get; set; }

        DbSet<TblParameter> TblParameters { get; set; }

        DbSet<TblDevice> TblDevices { get; set; }

        DbSet<TblDeviceModelConfig> TblDeviceModelConfigs { get; set; }

        DbSet<TblOverride> TblOverrides { get; set; }

        DbSet<TblOverrideDevice> TblOverrideDevices { get; set; }

        DbSet<TblOverrideContent> TblOverrideContents { get; set; }

        DbSet<TblOverrideDisplay> TblOverrideDisplays { get; set; }

        DbSet<TblAppPlatform> TblAppPlatforms { get; set; }

        DbSet<TblAppVersion> TblAppVersions { get; set; }
        DbSet<TblFirmwareImage> TblFirmwareImages { get; set; }
        DbSet<TblProvisioningPackage> TblProvisioningPackages { get; set; }
        DbSet<TblProvisioningAuthorization> TblProvisioningAuthorizations { get; set; }

        DbSet<TblUpdateDeployment> TblUpdateDeployments { get; set; }

        DbSet<TblUpdateDeploymentDevice> TblUpdateDeploymentDevices { get; set; }

        DbSet<TblUpdateDeploymentProgress> TblUpdateDeploymentProgress { get; set; }

        DbSet<TblContentHealthEvent> TblContentHealthEvents { get; set; }

        DbSet<TblDeviceType> TblDeviceTypes { get; set; }

        DbSet<TblApiKey> TblApiKeys { get; set; }

        DbSet<TblDeviceMetadata> TblDeviceMetadata { get; set; }

        DbSet<TblAssetApproval> TblAssetApprovals { get; set; }

        DbSet<TblUserGroup> TblUserGroups { get; set; }

        DbSet<TblUserGroupMembership> TblUserGroupMemberships { get; set; }

        DbSet<TblApprovalChain> TblApprovalChains { get; set; }

        DbSet<TblApprovalStage> TblApprovalStages { get; set; }

        DbSet<TblApprovalStageGroup> TblApprovalStageGroups { get; set; }

        DbSet<TblApprovalRequest> TblApprovalRequests { get; set; }

        DbSet<TblApprovalQueueEntry> TblApprovalQueueEntries { get; set; }

        DbSet<TblApprovalAction> TblApprovalActions { get; set; }

        DbSet<TblApprovalComment> TblApprovalComments { get; set; }

        DbSet<TblNotification> TblNotifications { get; set; }

        DbSet<TblSystemSetting> TblSystemSettings { get; set; }

        DbSet<TblUserMetadata> TblUserMetadata { get; set; }

        DbSet<TblUserGroupMetadata> TblUserGroupMetadata { get; set; }

        DbSet<TblLocationAccessRule> TblLocationAccessRules { get; set; }
        DbSet<TblLocationMetadataRule> TblLocationMetadataRules { get; set; }

        DbSet<TblAssetContentMetadata> TblAssetContentMetadata { get; set; }

        DbSet<TblDiscoveredAdGroup> TblDiscoveredAdGroups { get; set; }

        DbSet<TblFloorPlanElement> TblFloorPlanElements { get; set; }

        DbSet<TblFloorPlanIconSchedule> TblFloorPlanIconSchedules { get; set; }

        DbSet<TblAccessSchedule> TblAccessSchedules { get; set; }

        // Engage (Control Client) DbSets
        DbSet<TblControlClient> TblControlClients { get; set; }
        DbSet<TblControlClientDisplay> TblControlClientDisplays { get; set; }
        DbSet<TblControlClientPairingRequest> TblControlClientPairingRequests { get; set; }
        DbSet<TblControlClientToken> TblControlClientTokens { get; set; }
        DbSet<TblContentManifestLog> TblContentManifestLogs { get; set; }
        DbSet<TblDeviceCommand> TblDeviceCommands { get; set; }
        DbSet<TblDevicePairingRequest> TblDevicePairingRequests { get; set; }
        DbSet<TblDeviceToken> TblDeviceTokens { get; set; }

        // LLM Configuration DbSets
        DbSet<TblLlmInteraction> TblLlmInteractions { get; set; }
        DbSet<TblLlmPromptConfiguration> TblLlmPromptConfigurations { get; set; }
        DbSet<TblLlmProviderConfiguration> TblLlmProviderConfigurations { get; set; }

        // Cloud Platform Connector DbSets
        DbSet<TblCloudPlatformConnector> TblCloudPlatformConnectors { get; set; }

        // Event Log Archive
        DbSet<TblEventLogArchive> TblEventLogArchives { get; set; }

        // Metadata Definition
        DbSet<TblMetadataDefinition> TblMetadataDefinitions { get; set; }

        // User Notification Preferences
        DbSet<TblUserNotificationPreference> TblUserNotificationPreferences { get; set; }

        // Video Wall Subsystem
        DbSet<TblUserGroupLocationDefault> TblUserGroupLocationDefaults { get; set; }
        DbSet<TblUserGroupLocationLogo> TblUserGroupLocationLogos { get; set; }
        DbSet<TblUserGroupLocationLayerContent> TblUserGroupLocationLayerContents { get; set; }
        DbSet<TblDirectorUrl> TblDirectorUrls { get; set; }
        DbSet<TblWallLayoutMapping> TblWallLayoutMappings { get; set; }

        // Content Channel Subsystem
        DbSet<TblContentChannel> TblContentChannels { get; set; }
        DbSet<TblContentChannelMetadata> TblContentChannelMetadata { get; set; }

        // Location Bundle Subsystem
        DbSet<TblLocationBundle> TblLocationBundles { get; set; }
        DbSet<TblLocationBundleVersion> TblLocationBundleVersions { get; set; }
        DbSet<TblLocationBundleHistory> TblLocationBundleHistories { get; set; }
        DbSet<TblLocationBundleStorage> TblLocationBundleStorages { get; set; }

        // Panels
        DbSet<TblPanel> TblPanels { get; set; }

        // Messaging (GTAA FSD 1.1)
        DbSet<TblMessageType> TblMessageTypes { get; set; }
        DbSet<TblMessage> TblMessages { get; set; }
        DbSet<TblMessageText> TblMessageTexts { get; set; }
        DbSet<TblMessageActivation> TblMessageActivations { get; set; }
        DbSet<TblMessageActivationTarget> TblMessageActivationTargets { get; set; }
        DbSet<TblMessageActivationVersion> TblMessageActivationVersions { get; set; }
        DbSet<TblMessageActivationZone> TblMessageActivationZones { get; set; }
        DbSet<TblMessageAsset> TblMessageAssets { get; set; }
        DbSet<TblMessageRevision> TblMessageRevisions { get; set; }
        DbSet<TblMessageRevisionAsset> TblMessageRevisionAssets { get; set; }
        DbSet<TblMessageRevisionText> TblMessageRevisionTexts { get; set; }
        DbSet<TblMessageTypeAssetLibrary> TblMessageTypeAssetLibraries { get; set; }
        DbSet<TblMessageTypeLanguage> TblMessageTypeLanguages { get; set; }
        DbSet<TblMessagePermission> TblMessagePermissions { get; set; }
        DbSet<TblMessagingDeliveryState> TblMessagingDeliveryStates { get; set; }
        DbSet<TblZone> TblZones { get; set; }
        DbSet<TblZoneDisplay> TblZoneDisplays { get; set; }

        // CoreAPI and the Flight Data Editor (GTAA FSD 1.1)
        DbSet<TblCoreApiConnection> TblCoreApiConnections { get; set; }
        DbSet<TblCoreApiServer> TblCoreApiServers { get; set; }
        DbSet<TblCoreApiFeedState> TblCoreApiFeedStates { get; set; }

        // Approvals
        DbSet<TblApprovalReminder> TblApprovalReminders { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        Task<int> ExecuteSqlRawAsync(string sqlCommand, SqlParameter sqlParameter);
    }
}
