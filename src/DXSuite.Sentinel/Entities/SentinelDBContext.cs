using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

public partial class SentinelDBContext : DbContext, ISentinelDBContext
{
    public SentinelDBContext()
    {
    }

    public SentinelDBContext(DbContextOptions<SentinelDBContext> options)
        : base(options)
    {
    }

    public virtual DbSet<DeviceDescriptor> DeviceDescriptors { get; set; }
    public virtual DbSet<DeviceDescriptorVersion> DeviceDescriptorVersions { get; set; }
    public virtual DbSet<DeviceDescriptorTemplate> DeviceDescriptorTemplates { get; set; }
    public virtual DbSet<DeviceInstance> DeviceInstances { get; set; }
    public virtual DbSet<RemediationPolicy> RemediationPolicies { get; set; }
    public virtual DbSet<RemediationLog> RemediationLogs { get; set; }
    public virtual DbSet<AutomationDefinition> AutomationDefinitions { get; set; }
    public virtual DbSet<AutomationVersion> AutomationVersions { get; set; }
    public virtual DbSet<AutomationExecutionLog> AutomationExecutionLogs { get; set; }
    public virtual DbSet<ItsmAdapterConfig> ItsmAdapterConfigs { get; set; }
    public virtual DbSet<ItsmConnection> ItsmConnections { get; set; }
    public virtual DbSet<TicketTracking> TicketTrackings { get; set; }
    public virtual DbSet<OutageCorrelation> OutageCorrelations { get; set; }
    public virtual DbSet<PatternAnalysis> PatternAnalyses { get; set; }
    public virtual DbSet<FloorplanDevice> FloorplanDevices { get; set; }
    public virtual DbSet<SiteGateway> SiteGateways { get; set; }
    public virtual DbSet<SiteGatewayDeviceMapping> SiteGatewayDeviceMappings { get; set; }
    public virtual DbSet<PlatformDeviceLink> PlatformDeviceLinks { get; set; }
    public virtual DbSet<ProbeProfile> ProbeProfiles { get; set; }
    public virtual DbSet<ProbeHistory> ProbeHistories { get; set; }
    public virtual DbSet<ProbeHistorySummary> ProbeHistorySummaries { get; set; }
    public virtual DbSet<SentinelTask> SentinelTasks { get; set; }
    public virtual DbSet<SentinelTaskTarget> SentinelTaskTargets { get; set; }
    public virtual DbSet<SentinelTaskSchedule> SentinelTaskSchedules { get; set; }
    public virtual DbSet<SentinelTaskExecutionLog> SentinelTaskExecutionLogs { get; set; }
    public virtual DbSet<SentinelEnrollmentToken> SentinelEnrollmentTokens { get; set; }
    public virtual DbSet<DeviceSourceState> DeviceSourceStates { get; set; }
    public virtual DbSet<DeviceCorrelationSuggestion> DeviceCorrelationSuggestions { get; set; }
    public virtual DbSet<SentinelPulseIssueLevel> SentinelPulseIssueLevels { get; set; }
    public virtual DbSet<SentinelPulseDeviceState> SentinelPulseDeviceStates { get; set; }
    public virtual DbSet<SentinelPulseItemState> SentinelPulseItemStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DeviceDescriptor
        modelBuilder.Entity<DeviceDescriptor>(entity =>
        {
            entity.HasKey(e => e.DescriptorId);
            entity.HasIndex(e => e.DeviceType).IsUnique();
        });

        // DeviceDescriptorVersion
        modelBuilder.Entity<DeviceDescriptorVersion>(entity =>
        {
            entity.HasKey(e => e.VersionId);
            entity.HasOne(e => e.DeviceDescriptor)
                .WithMany(d => d.DeviceDescriptorVersions)
                .HasForeignKey(e => e.DescriptorId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.DescriptorId, e.Version }).IsUnique();
        });

        // DeviceDescriptorTemplate
        modelBuilder.Entity<DeviceDescriptorTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId);
            entity.HasIndex(e => e.TemplateName).IsUnique();
        });

        // DeviceInstance
        modelBuilder.Entity<DeviceInstance>(entity =>
        {
            entity.HasKey(e => e.DeviceInstanceId);
            entity.HasOne(e => e.DeviceDescriptor)
                .WithMany(d => d.DeviceInstances)
                .HasForeignKey(e => e.DescriptorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.DeviceDescriptorVersion)
                .WithMany(v => v.DeviceInstances)
                .HasForeignKey(e => e.DescriptorVersionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SiteGateway)
                .WithMany(g => g.DeviceInstances)
                .HasForeignKey(e => e.SiteGatewayId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.MacAddress);
            entity.HasIndex(e => e.DirectorDeviceId);
            entity.HasIndex(e => e.CustomerId);
        });

        // RemediationPolicy
        modelBuilder.Entity<RemediationPolicy>(entity =>
        {
            entity.HasKey(e => e.PolicyId);
            entity.HasIndex(e => e.CustomerId);
        });

        // RemediationLog
        modelBuilder.Entity<RemediationLog>(entity =>
        {
            entity.HasKey(e => e.LogId);
            entity.HasOne(e => e.DeviceInstance)
                .WithMany(d => d.RemediationLogs)
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AutomationDefinition)
                .WithMany(a => a.RemediationLogs)
                .HasForeignKey(e => e.AutomationId)
                .OnDelete(DeleteBehavior.SetNull);
            // LlmInteractionId is a logical reference to DirectorDB tblLlmInteraction
            entity.HasIndex(e => e.CreateDate);
            entity.HasIndex(e => e.DeviceInstanceId);
        });

        // AutomationDefinition
        modelBuilder.Entity<AutomationDefinition>(entity =>
        {
            entity.HasKey(e => e.AutomationId);
            entity.HasOne(e => e.DeviceDescriptor)
                .WithMany(d => d.AutomationDefinitions)
                .HasForeignKey(e => e.DeviceDescriptorId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.Status);
        });

        // AutomationVersion
        modelBuilder.Entity<AutomationVersion>(entity =>
        {
            entity.HasKey(e => e.VersionId);
            entity.HasOne(e => e.AutomationDefinition)
                .WithMany(a => a.AutomationVersions)
                .HasForeignKey(e => e.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.AutomationId, e.Version }).IsUnique();
        });

        // AutomationExecutionLog
        modelBuilder.Entity<AutomationExecutionLog>(entity =>
        {
            entity.HasKey(e => e.ExecutionId);
            entity.HasOne(e => e.AutomationDefinition)
                .WithMany(a => a.AutomationExecutionLogs)
                .HasForeignKey(e => e.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AutomationVersion)
                .WithMany(v => v.AutomationExecutionLogs)
                .HasForeignKey(e => e.AutomationVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.CreateDate);
        });

        // ItsmAdapterConfig
        modelBuilder.Entity<ItsmAdapterConfig>(entity =>
        {
            entity.HasKey(e => e.ConfigId);
        });

        // ItsmConnection
        modelBuilder.Entity<ItsmConnection>(entity =>
        {
            entity.HasKey(e => e.ConnectionId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => new { e.CustomerId, e.AdapterType });
        });

        // TicketTracking
        modelBuilder.Entity<TicketTracking>(entity =>
        {
            entity.HasKey(e => e.TrackingId);
            entity.HasOne(e => e.OutageCorrelation)
                .WithMany(o => o.TicketTrackings)
                .HasForeignKey(e => e.OutageCorrelationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.ExternalTicketId);
            entity.HasIndex(e => e.CustomerId);
        });

        // OutageCorrelation
        modelBuilder.Entity<OutageCorrelation>(entity =>
        {
            entity.HasKey(e => e.CorrelationId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.DetectedAt);
        });

        // PatternAnalysis
        modelBuilder.Entity<PatternAnalysis>(entity =>
        {
            entity.HasKey(e => e.AnalysisId);
            entity.HasIndex(e => e.CustomerId);
        });

        // FloorplanDevice
        modelBuilder.Entity<FloorplanDevice>(entity =>
        {
            entity.HasKey(e => e.FloorplanDeviceId);
            entity.HasOne(e => e.DeviceInstance)
                .WithMany(d => d.FloorplanDevices)
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.LocationId);
        });

        // SiteGateway
        modelBuilder.Entity<SiteGateway>(entity =>
        {
            entity.HasKey(e => e.GatewayId);
            entity.HasIndex(e => e.CustomerId);
        });

        // SiteGatewayDeviceMapping
        modelBuilder.Entity<SiteGatewayDeviceMapping>(entity =>
        {
            entity.HasKey(e => e.MappingId);
            entity.HasOne(e => e.SiteGateway)
                .WithMany(g => g.SiteGatewayDeviceMappings)
                .HasForeignKey(e => e.GatewayId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.DeviceInstance)
                .WithMany(d => d.SiteGatewayDeviceMappings)
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.GatewayId, e.DeviceInstanceId }).IsUnique();
        });

        // PlatformDeviceLink (ConnectorId is a logical reference to DirectorDB tblCloudPlatformConnector)
        modelBuilder.Entity<PlatformDeviceLink>(entity =>
        {
            entity.HasKey(e => e.LinkId);
            entity.HasOne(e => e.DeviceInstance)
                .WithMany(d => d.PlatformDeviceLinks)
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.MacAddress);
            entity.HasIndex(e => new { e.ConnectorId, e.PlatformDeviceId }).IsUnique();
        });

        // ProbeProfile
        modelBuilder.Entity<ProbeProfile>(entity =>
        {
            entity.HasKey(e => e.ProfileId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => new { e.Scope, e.MatchManufacturer, e.MatchModel });
            entity.HasIndex(e => e.DeviceInstanceId);
        });

        // ProbeHistory — high-volume table, indexed for cleanup and querying
        modelBuilder.Entity<ProbeHistory>(entity =>
        {
            entity.HasKey(e => e.ProbeHistoryId);
            entity.HasIndex(e => new { e.DeviceInstanceId, e.ProbeTimestamp });
            entity.HasIndex(e => new { e.DirectorDeviceId, e.ProbeTimestamp });
            entity.HasIndex(e => new { e.CustomerId, e.ProbeTimestamp });
            entity.HasIndex(e => e.CreateDate);
        });

        // ProbeHistorySummary — rolled-up trend data
        modelBuilder.Entity<ProbeHistorySummary>(entity =>
        {
            entity.HasKey(e => e.SummaryId);
            entity.HasIndex(e => new { e.DeviceInstanceId, e.Period, e.PeriodStart });
            entity.HasIndex(e => new { e.DirectorDeviceId, e.Period, e.PeriodStart });
            entity.HasIndex(e => new { e.CustomerId, e.Period, e.PeriodStart });
        });

        // ── Tasks (scripting engine) ──
        modelBuilder.Entity<SentinelTask>(entity =>
        {
            entity.HasKey(e => e.TaskId);
            entity.HasIndex(e => e.CustomerId);
        });

        modelBuilder.Entity<SentinelTaskTarget>(entity =>
        {
            entity.HasKey(e => e.TargetId);
            entity.HasOne(e => e.Task)
                .WithMany(t => t.Targets)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TargetScope, e.TargetRef });
        });

        modelBuilder.Entity<SentinelTaskSchedule>(entity =>
        {
            entity.HasKey(e => e.ScheduleId);
            entity.HasOne(e => e.Task)
                .WithMany(t => t.Schedules)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.IsEnabled, e.NextRunUtc });
        });

        modelBuilder.Entity<SentinelTaskExecutionLog>(entity =>
        {
            entity.HasKey(e => e.ExecutionId);
            entity.HasOne(e => e.Task)
                .WithMany(t => t.ExecutionLogs)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.CreateDate);
            entity.HasIndex(e => new { e.TaskId, e.CreateDate });
        });

        modelBuilder.Entity<SentinelEnrollmentToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.TokenHash);
        });

        // ── Multi-angle device identity ──
        modelBuilder.Entity<DeviceSourceState>(entity =>
        {
            entity.HasKey(e => e.DeviceSourceStateId);
            entity.HasOne(e => e.DeviceInstance)
                .WithMany(d => d.DeviceSourceStates)
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            // One row per (device, source angle) — makes WriteSourceState an idempotent upsert.
            entity.HasIndex(e => new { e.DeviceInstanceId, e.SourceType, e.SourceKey }).IsUnique();
            entity.HasIndex(e => e.CustomerId);
        });

        modelBuilder.Entity<DeviceCorrelationSuggestion>(entity =>
        {
            entity.HasKey(e => e.SuggestionId);
            entity.HasIndex(e => new { e.CustomerId, e.Status });
        });

        // ── Pulse reporting ──
        modelBuilder.Entity<SentinelPulseIssueLevel>(entity =>
        {
            // No filter: EF would otherwise exclude rows with any NULL key column, and every row has at
            // least one. SQL Server treats NULLs as equal here, which is the one-override-per-type rule.
            entity.HasIndex(e => new { e.Kind, e.MonitorPointId, e.Severity, e.DirectorAlertKind })
                .IsUnique()
                .HasFilter(null);
        });

        modelBuilder.Entity<SentinelPulseDeviceState>(entity =>
        {
            entity.HasOne<DeviceInstance>()
                .WithMany()
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SentinelPulseItemState>(entity =>
        {
            entity.HasOne<DeviceInstance>()
                .WithMany()
                .HasForeignKey(e => e.DeviceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
