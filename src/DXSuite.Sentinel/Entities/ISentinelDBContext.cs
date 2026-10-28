using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

public interface ISentinelDBContext
{
    DbSet<DeviceDescriptor> DeviceDescriptors { get; set; }
    DbSet<DeviceDescriptorVersion> DeviceDescriptorVersions { get; set; }
    DbSet<DeviceDescriptorTemplate> DeviceDescriptorTemplates { get; set; }
    DbSet<DeviceInstance> DeviceInstances { get; set; }
    DbSet<RemediationPolicy> RemediationPolicies { get; set; }
    DbSet<RemediationLog> RemediationLogs { get; set; }
    DbSet<AutomationDefinition> AutomationDefinitions { get; set; }
    DbSet<AutomationVersion> AutomationVersions { get; set; }
    DbSet<AutomationExecutionLog> AutomationExecutionLogs { get; set; }
    DbSet<ItsmAdapterConfig> ItsmAdapterConfigs { get; set; }
    DbSet<ItsmConnection> ItsmConnections { get; set; }
    DbSet<TicketTracking> TicketTrackings { get; set; }
    DbSet<OutageCorrelation> OutageCorrelations { get; set; }
    DbSet<PatternAnalysis> PatternAnalyses { get; set; }
    DbSet<FloorplanDevice> FloorplanDevices { get; set; }
    DbSet<SiteGateway> SiteGateways { get; set; }
    DbSet<SiteGatewayDeviceMapping> SiteGatewayDeviceMappings { get; set; }
    DbSet<PlatformDeviceLink> PlatformDeviceLinks { get; set; }
    DbSet<ProbeProfile> ProbeProfiles { get; set; }
    DbSet<ProbeHistory> ProbeHistories { get; set; }
    DbSet<ProbeHistorySummary> ProbeHistorySummaries { get; set; }
    DbSet<SentinelTask> SentinelTasks { get; set; }
    DbSet<SentinelTaskTarget> SentinelTaskTargets { get; set; }
    DbSet<SentinelTaskSchedule> SentinelTaskSchedules { get; set; }
    DbSet<SentinelTaskExecutionLog> SentinelTaskExecutionLogs { get; set; }
    DbSet<SentinelEnrollmentToken> SentinelEnrollmentTokens { get; set; }
    DbSet<DeviceSourceState> DeviceSourceStates { get; set; }
    DbSet<DeviceCorrelationSuggestion> DeviceCorrelationSuggestions { get; set; }
    DbSet<SentinelPulseIssueLevel> SentinelPulseIssueLevels { get; set; }
    DbSet<SentinelPulseDeviceState> SentinelPulseDeviceStates { get; set; }
    DbSet<SentinelPulseItemState> SentinelPulseItemStates { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
