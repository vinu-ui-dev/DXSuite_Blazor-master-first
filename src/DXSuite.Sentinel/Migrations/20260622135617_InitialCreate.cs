using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Sentinel.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentinelDeviceDescriptor",
                columns: table => new
                {
                    DescriptorID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Manufacturer = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Category = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CurrentVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelDeviceDescriptor", x => x.DescriptorID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelDeviceDescriptorTemplate",
                columns: table => new
                {
                    TemplateID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    TemplateJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelDeviceDescriptorTemplate", x => x.TemplateID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelItsmAdapterConfig",
                columns: table => new
                {
                    ConfigID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdapterType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    PlatformName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ConfigJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelItsmAdapterConfig", x => x.ConfigID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelItsmConnection",
                columns: table => new
                {
                    ConnectionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AdapterType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Server = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Password = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    Token = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ConfigJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelItsmConnection", x => x.ConnectionID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelOutageCorrelation",
                columns: table => new
                {
                    CorrelationID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CorrelationType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Scope = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "int", nullable: false),
                    DeviceInstanceIds = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelOutageCorrelation", x => x.CorrelationID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelPatternAnalysis",
                columns: table => new
                {
                    AnalysisID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PatternType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: true),
                    AnalysisJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    Severity = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelPatternAnalysis", x => x.AnalysisID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelProbeHistory",
                columns: table => new
                {
                    ProbeHistoryID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: true),
                    DirectorDeviceID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ProbeTimestamp = table.Column<DateTime>(type: "datetime", nullable: false),
                    OverallResult = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StatusColor = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ProfileName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ResultJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelProbeHistory", x => x.ProbeHistoryID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelProbeHistorySummary",
                columns: table => new
                {
                    SummaryID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: true),
                    DirectorDeviceID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Period = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime", nullable: false),
                    TotalProbes = table.Column<int>(type: "int", nullable: false),
                    PassedCount = table.Column<int>(type: "int", nullable: false),
                    DegradedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    UptimePercent = table.Column<float>(type: "real", nullable: false),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelProbeHistorySummary", x => x.SummaryID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelProbeProfile",
                columns: table => new
                {
                    ProfileID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    Scope = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    MatchManufacturer = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    MatchModel = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: true),
                    StrategyJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelProbeProfile", x => x.ProfileID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelRemediationPolicy",
                columns: table => new
                {
                    PolicyID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PolicyName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    PolicyJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    Scope = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelRemediationPolicy", x => x.PolicyID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelSiteGateway",
                columns: table => new
                {
                    GatewayID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SiteName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "datetime", nullable: true),
                    Version = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    ConfigJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelSiteGateway", x => x.GatewayID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelTask",
                columns: table => new
                {
                    TaskID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ScriptType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExecutionLocation = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ScriptContent = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    TimeoutMs = table.Column<int>(type: "int", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelTask", x => x.TaskID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelAutomationDefinition",
                columns: table => new
                {
                    AutomationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    AuthorType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DeviceDescriptorID = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    TriggerJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    CurrentVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelAutomationDefinition", x => x.AutomationID);
                    table.ForeignKey(
                        name: "FK_SentinelAutomationDefinition_SentinelDeviceDescriptor_DeviceDescriptorID",
                        column: x => x.DeviceDescriptorID,
                        principalTable: "SentinelDeviceDescriptor",
                        principalColumn: "DescriptorID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SentinelDeviceDescriptorVersion",
                columns: table => new
                {
                    VersionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DescriptorID = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DescriptorJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelDeviceDescriptorVersion", x => x.VersionID);
                    table.ForeignKey(
                        name: "FK_SentinelDeviceDescriptorVersion_SentinelDeviceDescriptor_DescriptorID",
                        column: x => x.DescriptorID,
                        principalTable: "SentinelDeviceDescriptor",
                        principalColumn: "DescriptorID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelTicketTracking",
                columns: table => new
                {
                    TrackingID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExternalTicketID = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ExternalTicketUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ItsmPlatform = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    DeviceInstanceIds = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    OutageCorrelationID = table.Column<long>(type: "bigint", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelTicketTracking", x => x.TrackingID);
                    table.ForeignKey(
                        name: "FK_SentinelTicketTracking_SentinelOutageCorrelation_OutageCorrelationID",
                        column: x => x.OutageCorrelationID,
                        principalTable: "SentinelOutageCorrelation",
                        principalColumn: "CorrelationID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SentinelTaskExecutionLog",
                columns: table => new
                {
                    ExecutionID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskID = table.Column<int>(type: "int", nullable: false),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: true),
                    GatewayID = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    OutputLog = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    TriggeredBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "datetime", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelTaskExecutionLog", x => x.ExecutionID);
                    table.ForeignKey(
                        name: "FK_SentinelTaskExecutionLog_SentinelTask_TaskID",
                        column: x => x.TaskID,
                        principalTable: "SentinelTask",
                        principalColumn: "TaskID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelTaskSchedule",
                columns: table => new
                {
                    ScheduleID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskID = table.Column<int>(type: "int", nullable: false),
                    ExecutionMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    FrequencySeconds = table.Column<int>(type: "int", nullable: true),
                    DaysOfWeek = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    TimeOfDay = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastRunUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    NextRunUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelTaskSchedule", x => x.ScheduleID);
                    table.ForeignKey(
                        name: "FK_SentinelTaskSchedule_SentinelTask_TaskID",
                        column: x => x.TaskID,
                        principalTable: "SentinelTask",
                        principalColumn: "TaskID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelTaskTarget",
                columns: table => new
                {
                    TargetID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskID = table.Column<int>(type: "int", nullable: false),
                    TargetScope = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TargetRef = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelTaskTarget", x => x.TargetID);
                    table.ForeignKey(
                        name: "FK_SentinelTaskTarget_SentinelTask_TaskID",
                        column: x => x.TaskID,
                        principalTable: "SentinelTask",
                        principalColumn: "TaskID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelAutomationVersion",
                columns: table => new
                {
                    VersionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutomationID = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ContentJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelAutomationVersion", x => x.VersionID);
                    table.ForeignKey(
                        name: "FK_SentinelAutomationVersion_SentinelAutomationDefinition_AutomationID",
                        column: x => x.AutomationID,
                        principalTable: "SentinelAutomationDefinition",
                        principalColumn: "AutomationID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelDeviceInstance",
                columns: table => new
                {
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DescriptorID = table.Column<int>(type: "int", nullable: false),
                    DescriptorVersionID = table.Column<int>(type: "int", nullable: true),
                    DirectorDeviceID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    PulseDeviceID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    PulseCustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    DisplayName = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: false),
                    IpAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    MacAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    CredentialsJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    ConfigJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LocationID = table.Column<int>(type: "int", nullable: true),
                    SiteGatewayID = table.Column<int>(type: "int", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastStateJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    LastStateUpdated = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CommissionedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CommissionedSource = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelDeviceInstance", x => x.DeviceInstanceID);
                    table.ForeignKey(
                        name: "FK_SentinelDeviceInstance_SentinelDeviceDescriptorVersion_DescriptorVersionID",
                        column: x => x.DescriptorVersionID,
                        principalTable: "SentinelDeviceDescriptorVersion",
                        principalColumn: "VersionID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SentinelDeviceInstance_SentinelDeviceDescriptor_DescriptorID",
                        column: x => x.DescriptorID,
                        principalTable: "SentinelDeviceDescriptor",
                        principalColumn: "DescriptorID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SentinelDeviceInstance_SentinelSiteGateway_SiteGatewayID",
                        column: x => x.SiteGatewayID,
                        principalTable: "SentinelSiteGateway",
                        principalColumn: "GatewayID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SentinelAutomationExecutionLog",
                columns: table => new
                {
                    ExecutionID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutomationID = table.Column<int>(type: "int", nullable: false),
                    AutomationVersionID = table.Column<int>(type: "int", nullable: false),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StepsExecuted = table.Column<int>(type: "int", nullable: false),
                    ExecutionTraceJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelAutomationExecutionLog", x => x.ExecutionID);
                    table.ForeignKey(
                        name: "FK_SentinelAutomationExecutionLog_SentinelAutomationDefinition_AutomationID",
                        column: x => x.AutomationID,
                        principalTable: "SentinelAutomationDefinition",
                        principalColumn: "AutomationID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SentinelAutomationExecutionLog_SentinelAutomationVersion_AutomationVersionID",
                        column: x => x.AutomationVersionID,
                        principalTable: "SentinelAutomationVersion",
                        principalColumn: "VersionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SentinelFloorplanDevice",
                columns: table => new
                {
                    FloorplanDeviceID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    LocationID = table.Column<int>(type: "int", nullable: false),
                    PositionX = table.Column<double>(type: "float", nullable: false),
                    PositionY = table.Column<double>(type: "float", nullable: false),
                    Rotation = table.Column<double>(type: "float", nullable: false),
                    Scale = table.Column<double>(type: "float", nullable: false),
                    Label = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelFloorplanDevice", x => x.FloorplanDeviceID);
                    table.ForeignKey(
                        name: "FK_SentinelFloorplanDevice_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelPlatformDeviceLink",
                columns: table => new
                {
                    LinkID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectorID = table.Column<int>(type: "int", nullable: false),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    PlatformDeviceID = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    MacAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    PlatformMetadataJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelPlatformDeviceLink", x => x.LinkID);
                    table.ForeignKey(
                        name: "FK_SentinelPlatformDeviceLink_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelRemediationLog",
                columns: table => new
                {
                    LogID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    TriggerCondition = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    ActionTaken = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    ActionParameters = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    Result = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ResultDetails = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    AutomationID = table.Column<int>(type: "int", nullable: true),
                    LlmInteractionID = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelRemediationLog", x => x.LogID);
                    table.ForeignKey(
                        name: "FK_SentinelRemediationLog_SentinelAutomationDefinition_AutomationID",
                        column: x => x.AutomationID,
                        principalTable: "SentinelAutomationDefinition",
                        principalColumn: "AutomationID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SentinelRemediationLog_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelSiteGatewayDeviceMapping",
                columns: table => new
                {
                    MappingID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GatewayID = table.Column<int>(type: "int", nullable: false),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    LanIpAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    LanPort = table.Column<int>(type: "int", nullable: true),
                    IsReachable = table.Column<bool>(type: "bit", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelSiteGatewayDeviceMapping", x => x.MappingID);
                    table.ForeignKey(
                        name: "FK_SentinelSiteGatewayDeviceMapping_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SentinelSiteGatewayDeviceMapping_SentinelSiteGateway_GatewayID",
                        column: x => x.GatewayID,
                        principalTable: "SentinelSiteGateway",
                        principalColumn: "GatewayID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelAutomationDefinition_DeviceDescriptorID",
                table: "SentinelAutomationDefinition",
                column: "DeviceDescriptorID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelAutomationDefinition_Status",
                table: "SentinelAutomationDefinition",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelAutomationExecutionLog_AutomationID",
                table: "SentinelAutomationExecutionLog",
                column: "AutomationID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelAutomationExecutionLog_AutomationVersionID",
                table: "SentinelAutomationExecutionLog",
                column: "AutomationVersionID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelAutomationExecutionLog_CreateDate",
                table: "SentinelAutomationExecutionLog",
                column: "CreateDate");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelAutomationVersion_AutomationID_Version",
                table: "SentinelAutomationVersion",
                columns: new[] { "AutomationID", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceDescriptor_DeviceType",
                table: "SentinelDeviceDescriptor",
                column: "DeviceType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceDescriptorTemplate_TemplateName",
                table: "SentinelDeviceDescriptorTemplate",
                column: "TemplateName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceDescriptorVersion_DescriptorID_Version",
                table: "SentinelDeviceDescriptorVersion",
                columns: new[] { "DescriptorID", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceInstance_CustomerID",
                table: "SentinelDeviceInstance",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceInstance_DescriptorID",
                table: "SentinelDeviceInstance",
                column: "DescriptorID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceInstance_DescriptorVersionID",
                table: "SentinelDeviceInstance",
                column: "DescriptorVersionID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceInstance_DirectorDeviceID",
                table: "SentinelDeviceInstance",
                column: "DirectorDeviceID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceInstance_MacAddress",
                table: "SentinelDeviceInstance",
                column: "MacAddress");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceInstance_SiteGatewayID",
                table: "SentinelDeviceInstance",
                column: "SiteGatewayID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelFloorplanDevice_DeviceInstanceID",
                table: "SentinelFloorplanDevice",
                column: "DeviceInstanceID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelFloorplanDevice_LocationID",
                table: "SentinelFloorplanDevice",
                column: "LocationID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelItsmConnection_CustomerID",
                table: "SentinelItsmConnection",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelItsmConnection_CustomerID_AdapterType",
                table: "SentinelItsmConnection",
                columns: new[] { "CustomerID", "AdapterType" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelOutageCorrelation_CustomerID",
                table: "SentinelOutageCorrelation",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelOutageCorrelation_DetectedAt",
                table: "SentinelOutageCorrelation",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelPatternAnalysis_CustomerID",
                table: "SentinelPatternAnalysis",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelPlatformDeviceLink_ConnectorID_PlatformDeviceID",
                table: "SentinelPlatformDeviceLink",
                columns: new[] { "ConnectorID", "PlatformDeviceID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelPlatformDeviceLink_DeviceInstanceID",
                table: "SentinelPlatformDeviceLink",
                column: "DeviceInstanceID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelPlatformDeviceLink_MacAddress",
                table: "SentinelPlatformDeviceLink",
                column: "MacAddress");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistory_CreateDate",
                table: "SentinelProbeHistory",
                column: "CreateDate");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistory_CustomerID_ProbeTimestamp",
                table: "SentinelProbeHistory",
                columns: new[] { "CustomerID", "ProbeTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistory_DeviceInstanceID_ProbeTimestamp",
                table: "SentinelProbeHistory",
                columns: new[] { "DeviceInstanceID", "ProbeTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistory_DirectorDeviceID_ProbeTimestamp",
                table: "SentinelProbeHistory",
                columns: new[] { "DirectorDeviceID", "ProbeTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistorySummary_CustomerID_Period_PeriodStart",
                table: "SentinelProbeHistorySummary",
                columns: new[] { "CustomerID", "Period", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistorySummary_DeviceInstanceID_Period_PeriodStart",
                table: "SentinelProbeHistorySummary",
                columns: new[] { "DeviceInstanceID", "Period", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeHistorySummary_DirectorDeviceID_Period_PeriodStart",
                table: "SentinelProbeHistorySummary",
                columns: new[] { "DirectorDeviceID", "Period", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeProfile_CustomerID",
                table: "SentinelProbeProfile",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeProfile_DeviceInstanceID",
                table: "SentinelProbeProfile",
                column: "DeviceInstanceID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelProbeProfile_Scope_MatchManufacturer_MatchModel",
                table: "SentinelProbeProfile",
                columns: new[] { "Scope", "MatchManufacturer", "MatchModel" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelRemediationLog_AutomationID",
                table: "SentinelRemediationLog",
                column: "AutomationID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelRemediationLog_CreateDate",
                table: "SentinelRemediationLog",
                column: "CreateDate");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelRemediationLog_DeviceInstanceID",
                table: "SentinelRemediationLog",
                column: "DeviceInstanceID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelRemediationPolicy_CustomerID",
                table: "SentinelRemediationPolicy",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelSiteGateway_CustomerID",
                table: "SentinelSiteGateway",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelSiteGatewayDeviceMapping_DeviceInstanceID",
                table: "SentinelSiteGatewayDeviceMapping",
                column: "DeviceInstanceID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelSiteGatewayDeviceMapping_GatewayID_DeviceInstanceID",
                table: "SentinelSiteGatewayDeviceMapping",
                columns: new[] { "GatewayID", "DeviceInstanceID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTask_CustomerID",
                table: "SentinelTask",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTaskExecutionLog_CreateDate",
                table: "SentinelTaskExecutionLog",
                column: "CreateDate");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTaskExecutionLog_TaskID_CreateDate",
                table: "SentinelTaskExecutionLog",
                columns: new[] { "TaskID", "CreateDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTaskSchedule_IsEnabled_NextRunUtc",
                table: "SentinelTaskSchedule",
                columns: new[] { "IsEnabled", "NextRunUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTaskSchedule_TaskID",
                table: "SentinelTaskSchedule",
                column: "TaskID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTaskTarget_TargetScope_TargetRef",
                table: "SentinelTaskTarget",
                columns: new[] { "TargetScope", "TargetRef" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTaskTarget_TaskID",
                table: "SentinelTaskTarget",
                column: "TaskID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTicketTracking_CustomerID",
                table: "SentinelTicketTracking",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTicketTracking_ExternalTicketID",
                table: "SentinelTicketTracking",
                column: "ExternalTicketID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelTicketTracking_OutageCorrelationID",
                table: "SentinelTicketTracking",
                column: "OutageCorrelationID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentinelAutomationExecutionLog");

            migrationBuilder.DropTable(
                name: "SentinelDeviceDescriptorTemplate");

            migrationBuilder.DropTable(
                name: "SentinelFloorplanDevice");

            migrationBuilder.DropTable(
                name: "SentinelItsmAdapterConfig");

            migrationBuilder.DropTable(
                name: "SentinelItsmConnection");

            migrationBuilder.DropTable(
                name: "SentinelPatternAnalysis");

            migrationBuilder.DropTable(
                name: "SentinelPlatformDeviceLink");

            migrationBuilder.DropTable(
                name: "SentinelProbeHistory");

            migrationBuilder.DropTable(
                name: "SentinelProbeHistorySummary");

            migrationBuilder.DropTable(
                name: "SentinelProbeProfile");

            migrationBuilder.DropTable(
                name: "SentinelRemediationLog");

            migrationBuilder.DropTable(
                name: "SentinelRemediationPolicy");

            migrationBuilder.DropTable(
                name: "SentinelSiteGatewayDeviceMapping");

            migrationBuilder.DropTable(
                name: "SentinelTaskExecutionLog");

            migrationBuilder.DropTable(
                name: "SentinelTaskSchedule");

            migrationBuilder.DropTable(
                name: "SentinelTaskTarget");

            migrationBuilder.DropTable(
                name: "SentinelTicketTracking");

            migrationBuilder.DropTable(
                name: "SentinelAutomationVersion");

            migrationBuilder.DropTable(
                name: "SentinelDeviceInstance");

            migrationBuilder.DropTable(
                name: "SentinelTask");

            migrationBuilder.DropTable(
                name: "SentinelOutageCorrelation");

            migrationBuilder.DropTable(
                name: "SentinelAutomationDefinition");

            migrationBuilder.DropTable(
                name: "SentinelDeviceDescriptorVersion");

            migrationBuilder.DropTable(
                name: "SentinelSiteGateway");

            migrationBuilder.DropTable(
                name: "SentinelDeviceDescriptor");
        }
    }
}
