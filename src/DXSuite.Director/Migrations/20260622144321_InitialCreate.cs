using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblAppPlatform",
                columns: table => new
                {
                    PlatformId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    PlatformDescription = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    FolderName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAppPlatform", x => x.PlatformId);
                });

            migrationBuilder.CreateTable(
                name: "tblAssetGroup",
                columns: table => new
                {
                    AssetGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    AssetGroupName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAssetGroup", x => x.AssetGroupId);
                });

            migrationBuilder.CreateTable(
                name: "tblAssetType",
                columns: table => new
                {
                    AssetTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetTypeName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAssetType", x => x.AssetTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblCloudPlatformConnector",
                columns: table => new
                {
                    ConnectorID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ConfigJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    LastSyncMessage = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    GatewayMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "none"),
                    ContentMode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false, defaultValue: "monitoring-only"),
                    EnabledImports = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    CredentialExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblCloudPlatformConnector", x => x.ConnectorID);
                });

            migrationBuilder.CreateTable(
                name: "tblContentPackageEnvironmentLabel",
                columns: table => new
                {
                    ContentPackageEnvironmentLabelId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedByUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblContentPackageEnvironmentLabel", x => x.ContentPackageEnvironmentLabelId);
                });

            migrationBuilder.CreateTable(
                name: "tblContentPackageHistory",
                columns: table => new
                {
                    ContentPackageHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Direction = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    BundleFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    InitiatedByUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ExportedByUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SourceEnvironment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScopeDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalRecords = table.Column<int>(type: "int", nullable: false),
                    InsertedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblContentPackageHistory", x => x.ContentPackageHistoryId);
                });

            migrationBuilder.CreateTable(
                name: "tblDeviceModelConfig",
                columns: table => new
                {
                    DeviceModelConfigId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Manufacturer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProfilesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDeviceModelConfig", x => x.DeviceModelConfigId);
                });

            migrationBuilder.CreateTable(
                name: "tblDeviceType",
                columns: table => new
                {
                    DeviceTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDeviceType", x => x.DeviceTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblDisplayGroup",
                columns: table => new
                {
                    DisplayGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    DisplayGroupName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 99),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDisplayGroup", x => x.DisplayGroupId);
                });

            migrationBuilder.CreateTable(
                name: "tblDisplayType",
                columns: table => new
                {
                    DisplayTypeId = table.Column<int>(type: "int", nullable: false),
                    DisplayTypeName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDisplayType", x => x.DisplayTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblEndpoint",
                columns: table => new
                {
                    EndpointId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    IPAddress = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    SerialNo = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblEndpoint", x => x.EndpointId);
                });

            migrationBuilder.CreateTable(
                name: "tblEventLogArchive",
                columns: table => new
                {
                    ArchiveId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalRecordId = table.Column<int>(type: "int", nullable: false),
                    EventSource = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    EventTypeId = table.Column<int>(type: "int", nullable: false),
                    EventTitle = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    EventMessage = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    EventSeverityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    DisplayId = table.Column<int>(type: "int", nullable: true),
                    ScheduleEventId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: true),
                    TicketCreated = table.Column<bool>(type: "bit", nullable: false),
                    TicketId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ArchivedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblEventLogArchive", x => x.ArchiveId);
                });

            migrationBuilder.CreateTable(
                name: "tblEventSeverity",
                columns: table => new
                {
                    EventSeverityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeverityName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblEventSeverity", x => x.EventSeverityId);
                });

            migrationBuilder.CreateTable(
                name: "tblEventType",
                columns: table => new
                {
                    EventTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventTypeName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblEventType", x => x.EventTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblLlmInteraction",
                columns: table => new
                {
                    InteractionID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    PromptHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PromptTokens = table.Column<int>(type: "int", nullable: false),
                    CompletionTokens = table.Column<int>(type: "int", nullable: false),
                    RequestJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    ResponseJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    Purpose = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLlmInteraction", x => x.InteractionID);
                });

            migrationBuilder.CreateTable(
                name: "tblLlmPromptConfig",
                columns: table => new
                {
                    PromptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UseCaseId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Temperature = table.Column<float>(type: "real", nullable: false),
                    MaxTokens = table.Column<int>(type: "int", nullable: false),
                    ResponseFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SupportsText = table.Column<bool>(type: "bit", nullable: false),
                    SupportsVision = table.Column<bool>(type: "bit", nullable: false),
                    LastTestedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEditUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLlmPromptConfig", x => x.PromptId);
                });

            migrationBuilder.CreateTable(
                name: "tblLlmProviderConfig",
                columns: table => new
                {
                    ConfigID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxTokens = table.Column<int>(type: "int", nullable: false),
                    DefaultTemperature = table.Column<float>(type: "real", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CredentialExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLlmProviderConfig", x => x.ConfigID);
                });

            migrationBuilder.CreateTable(
                name: "tblLocationGroup",
                columns: table => new
                {
                    LocationGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationGroupName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 99),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationGroup", x => x.LocationGroupId);
                });

            migrationBuilder.CreateTable(
                name: "tblLocationType",
                columns: table => new
                {
                    LocationTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationTypeName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    LinkedToDisplay = table.Column<bool>(type: "bit", nullable: false),
                    IsCustom = table.Column<bool>(type: "bit", nullable: false),
                    ComponentTypeName = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    ShowOverrideSection = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationType", x => x.LocationTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblMetadataDefinition",
                columns: table => new
                {
                    MetadataDefinitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    FieldType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMetadataDefinition", x => x.MetadataDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "tblOverride",
                columns: table => new
                {
                    OverrideId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OverrideName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsScheduled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    StartDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    Sunday = table.Column<bool>(type: "bit", nullable: false),
                    Monday = table.Column<bool>(type: "bit", nullable: false),
                    Tuesday = table.Column<bool>(type: "bit", nullable: false),
                    Wednesday = table.Column<bool>(type: "bit", nullable: false),
                    Thursday = table.Column<bool>(type: "bit", nullable: false),
                    Friday = table.Column<bool>(type: "bit", nullable: false),
                    Saturday = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastScheduleState = table.Column<bool>(type: "bit", nullable: true),
                    LastScheduleActivation = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastScheduleDeactivation = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblOverride", x => x.OverrideId);
                });

            migrationBuilder.CreateTable(
                name: "tblParameter",
                columns: table => new
                {
                    RecordID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    KeyName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblParameter", x => x.RecordID);
                });

            migrationBuilder.CreateTable(
                name: "tblSystemSetting",
                columns: table => new
                {
                    SettingKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblSystemSetting", x => x.SettingKey);
                });

            migrationBuilder.CreateTable(
                name: "tblUserGroup",
                columns: table => new
                {
                    UserGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    GroupType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false, defaultValue: "director"),
                    ExternalId = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserGroup", x => x.UserGroupId);
                });

            migrationBuilder.CreateTable(
                name: "tblUserRole",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    RoleDescription = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    IsGlobal = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserRole", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "tblAppVersion",
                columns: table => new
                {
                    AppVersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    VersionName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ReleaseNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZipFileName = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    ZipFilePath = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    ZipFileSize = table.Column<long>(type: "bigint", nullable: true),
                    ZipFileHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    MinPreviousVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ModifiedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAppVersion", x => x.AppVersionId);
                    table.ForeignKey(
                        name: "FK_tblAppVersion_Platform",
                        column: x => x.PlatformId,
                        principalTable: "tblAppPlatform",
                        principalColumn: "PlatformId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblContentPackageHistoryItem",
                columns: table => new
                {
                    ContentPackageHistoryItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageHistoryId = table.Column<int>(type: "int", nullable: false),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SourceUserPrincipalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblContentPackageHistoryItem", x => x.ContentPackageHistoryItemId);
                    table.ForeignKey(
                        name: "FK_tblContentPackageHistoryItem_tblContentPackageHistory_ContentPackageHistoryId",
                        column: x => x.ContentPackageHistoryId,
                        principalTable: "tblContentPackageHistory",
                        principalColumn: "ContentPackageHistoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDiscoveredAdGroup",
                columns: table => new
                {
                    DiscoveredAdGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObjectId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    FirstSeenDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    LastSeenDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    IsDismissed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsAdopted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AdoptedUserGroupId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDiscoveredAdGroup", x => x.DiscoveredAdGroupId);
                    table.ForeignKey(
                        name: "FK_DiscoveredAdGroup_UserGroup",
                        column: x => x.AdoptedUserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tblLocation",
                columns: table => new
                {
                    LocationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    LocationTypeId = table.Column<int>(type: "int", nullable: false),
                    LocationGroupId = table.Column<int>(type: "int", nullable: false),
                    LocationImagePath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 99),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    BackgroundImagePath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    DisplayAnchorTop = table.Column<double>(type: "float", nullable: true),
                    DisplayAnchorLeft = table.Column<double>(type: "float", nullable: true),
                    DisplayOffset = table.Column<double>(type: "float", nullable: true),
                    BackgroundImageHeight = table.Column<double>(type: "float", nullable: true),
                    BackgroundImageWidth = table.Column<double>(type: "float", nullable: true),
                    EnableShuffle = table.Column<bool>(type: "bit", nullable: false),
                    EnableFade = table.Column<bool>(type: "bit", nullable: false),
                    EnableContentApproval = table.Column<bool>(type: "bit", nullable: false),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsTransientCluster = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ParentVideoWallLocationId = table.Column<int>(type: "int", nullable: true),
                    ClusterKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    OwningUserGroupId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocation", x => x.LocationId);
                    table.ForeignKey(
                        name: "FK_tblLocation_OwningUserGroup",
                        column: x => x.OwningUserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblLocation_ParentVideoWall",
                        column: x => x.ParentVideoWallLocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblLocation_tblLocationGroup",
                        column: x => x.LocationGroupId,
                        principalTable: "tblLocationGroup",
                        principalColumn: "LocationGroupId");
                    table.ForeignKey(
                        name: "FK_tblLocation_tblLocationType1",
                        column: x => x.LocationTypeId,
                        principalTable: "tblLocationType",
                        principalColumn: "LocationTypeId");
                });

            migrationBuilder.CreateTable(
                name: "tblUserGroupMetadata",
                columns: table => new
                {
                    UserGroupMetadataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MetadataValue = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserGroupMetadata", x => x.UserGroupMetadataId);
                    table.ForeignKey(
                        name: "FK_UserGroupMetadata_UserGroup",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblUser",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    GivenName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Surname = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    LastLoginTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    UserPrincipalName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    Title = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    AirlineCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    PulseAccess = table.Column<bool>(type: "bit", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUser", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_tblUser_tblUserRole",
                        column: x => x.RoleId,
                        principalTable: "tblUserRole",
                        principalColumn: "RoleId");
                });

            migrationBuilder.CreateTable(
                name: "tblAirlineLocation",
                columns: table => new
                {
                    AirlineLocationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    AirlineCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAirlineLocation_1", x => x.AirlineLocationId);
                    table.ForeignKey(
                        name: "FK_tblAirlineLocation_tblLocation1",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                });

            migrationBuilder.CreateTable(
                name: "tblConstraint",
                columns: table => new
                {
                    ConstraintId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    ConstraintName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    OwningLocationId = table.Column<int>(type: "int", nullable: true),
                    IsGlobal = table.Column<bool>(type: "bit", nullable: false),
                    ResolutionWidth = table.Column<int>(type: "int", nullable: true),
                    ResolutionHeight = table.Column<int>(type: "int", nullable: true),
                    ResolutionMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblConstraint", x => x.ConstraintId);
                    table.ForeignKey(
                        name: "FK_tblConstraint_tblLocation_OwningLocationId",
                        column: x => x.OwningLocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                });

            migrationBuilder.CreateTable(
                name: "tblUpdateDeployment",
                columns: table => new
                {
                    DeploymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppVersionId = table.Column<int>(type: "int", nullable: false),
                    DeploymentName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeploymentScope = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ScheduledDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ModifiedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    EnableBatching = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    BatchSize = table.Column<int>(type: "int", nullable: false, defaultValue: 10),
                    BatchStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CurrentBatchNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TotalBatches = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    BatchStartedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastBatchCompletedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUpdateDeployment", x => x.DeploymentId);
                    table.ForeignKey(
                        name: "FK_tblUpdateDeployment_AppVersion",
                        column: x => x.AppVersionId,
                        principalTable: "tblAppVersion",
                        principalColumn: "AppVersionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblUpdateDeployment_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tblApiKey",
                columns: table => new
                {
                    ApiKeyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ApiKeyHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    KeyPrefix = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    KeyName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Permissions = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CallCount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApiKey", x => x.ApiKeyId);
                    table.ForeignKey(
                        name: "FK_tblApiKey_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblApiKey_tblUser",
                        column: x => x.CreatedBy,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tblControlClient",
                columns: table => new
                {
                    ControlClientId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AccessCodeHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    AccessCodePrefix = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastAuthAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastAuthIp = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblControlClient", x => x.ControlClientId);
                    table.ForeignKey(
                        name: "FK_ControlClient_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ControlClient_User",
                        column: x => x.CreatedBy,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tblDevicePairingRequest",
                columns: table => new
                {
                    PairingRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PairingCodeHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "pending"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Manufacturer = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    Model = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    SerialNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    MacAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDevicePairingRequest", x => x.PairingRequestId);
                    table.ForeignKey(
                        name: "FK_DevicePairingRequest_ResolvedBy",
                        column: x => x.ResolvedByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblLocalAccount",
                columns: table => new
                {
                    LocalAccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastLoginDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ConfirmationToken = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ConfirmationTokenExpiry = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocalAccount", x => x.LocalAccountId);
                    table.ForeignKey(
                        name: "FK_tblLocalAccount_tblUser_UserId",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblLocationAccessRule",
                columns: table => new
                {
                    LocationAccessRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserGroupId = table.Column<int>(type: "int", nullable: true),
                    AccessLevel = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "full"),
                    MetadataFilterKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationAccessRule", x => x.LocationAccessRuleId);
                    table.ForeignKey(
                        name: "FK_AccessRule_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_AccessRule_User",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_AccessRule_UserGroup",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId");
                });

            migrationBuilder.CreateTable(
                name: "tblNotification",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NotificationType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    ReferenceUrl = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    EmailSent = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblNotification", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notification_User",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblUserGroupMembership",
                columns: table => new
                {
                    MembershipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserGroupMembership", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_UserGroupMembership_User",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroupMembership_UserGroup",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblUserLocation",
                columns: table => new
                {
                    UserLocationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserLocation", x => x.UserLocationId);
                    table.ForeignKey(
                        name: "FK_tblUserLocation_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_tblUserLocation_tblUser",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblUserMetadata",
                columns: table => new
                {
                    UserMetadataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MetadataValue = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserMetadata", x => x.UserMetadataId);
                    table.ForeignKey(
                        name: "FK_UserMetadata_User",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblUserNotificationPreference",
                columns: table => new
                {
                    UserNotificationPreferenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserNotificationPreference", x => x.UserNotificationPreferenceId);
                    table.ForeignKey(
                        name: "FK_NotificationPreference_User",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblAsset",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    AssetName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    AssetPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    AssetInkPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    AssetGroupId = table.Column<int>(type: "int", nullable: false),
                    AssetTypeId = table.Column<int>(type: "int", nullable: false),
                    AssetHeight = table.Column<double>(type: "float", nullable: false, defaultValue: 1080.0),
                    AssetWidth = table.Column<double>(type: "float", nullable: false, defaultValue: 1920.0),
                    ConstraintId = table.Column<int>(type: "int", nullable: false),
                    Duration = table.Column<double>(type: "float", nullable: false, defaultValue: 7.0),
                    VideoMaskPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    EndpointId = table.Column<int>(type: "int", nullable: true),
                    EndpointPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    WebRenderMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    EnableHealthMonitoring = table.Column<bool>(type: "bit", nullable: false),
                    ContentSourceUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 99),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAsset", x => x.AssetId);
                    table.ForeignKey(
                        name: "FK_tblAsset_tblAssetGroup",
                        column: x => x.AssetGroupId,
                        principalTable: "tblAssetGroup",
                        principalColumn: "AssetGroupId");
                    table.ForeignKey(
                        name: "FK_tblAsset_tblAssetType",
                        column: x => x.AssetTypeId,
                        principalTable: "tblAssetType",
                        principalColumn: "AssetTypeId");
                    table.ForeignKey(
                        name: "FK_tblAsset_tblConstraint",
                        column: x => x.ConstraintId,
                        principalTable: "tblConstraint",
                        principalColumn: "ConstraintId");
                    table.ForeignKey(
                        name: "FK_tblAsset_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                });

            migrationBuilder.CreateTable(
                name: "tblUserGroupLocationDefault",
                columns: table => new
                {
                    UserGroupLocationDefaultId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    DefaultConstraintId = table.Column<int>(type: "int", nullable: true),
                    BackgroundPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BackgroundMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "fill-width"),
                    MaskPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MaskMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "fill-width"),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserGroupLocationDefault", x => x.UserGroupLocationDefaultId);
                    table.ForeignKey(
                        name: "FK_tblUserGroupLocationDefault_Constraint",
                        column: x => x.DefaultConstraintId,
                        principalTable: "tblConstraint",
                        principalColumn: "ConstraintId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblUserGroupLocationDefault_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblUserGroupLocationDefault_UserGroup",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblControlClientPairingRequest",
                columns: table => new
                {
                    PairingRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PairingCodeHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "pending"),
                    ControlClientId = table.Column<int>(type: "int", nullable: true),
                    ClientIp = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    ClientHostname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblControlClientPairingRequest", x => x.PairingRequestId);
                    table.ForeignKey(
                        name: "FK_CCPairingRequest_Client",
                        column: x => x.ControlClientId,
                        principalTable: "tblControlClient",
                        principalColumn: "ControlClientId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CCPairingRequest_ResolvedBy",
                        column: x => x.ResolvedByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblControlClientToken",
                columns: table => new
                {
                    ControlClientTokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ControlClientId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    TokenPrefix = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblControlClientToken", x => x.ControlClientTokenId);
                    table.ForeignKey(
                        name: "FK_ControlClientToken_Client",
                        column: x => x.ControlClientId,
                        principalTable: "tblControlClient",
                        principalColumn: "ControlClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalChain",
                columns: table => new
                {
                    ApprovalChainId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LocationAccessRuleId = table.Column<int>(type: "int", nullable: true),
                    AutoApproveMembers = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    EnableLlmAutoApproval = table.Column<bool>(type: "bit", nullable: false),
                    LlmAutoApprovalUpToStageOrder = table.Column<int>(type: "int", nullable: false),
                    DeclinedContentRetentionDays = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalChain", x => x.ApprovalChainId);
                    table.ForeignKey(
                        name: "FK_ApprovalChain_AccessRule",
                        column: x => x.LocationAccessRuleId,
                        principalTable: "tblLocationAccessRule",
                        principalColumn: "LocationAccessRuleId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApprovalChain_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblAssetApproval",
                columns: table => new
                {
                    AssetApprovalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ReviewedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReviewNotes = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    DeclinedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAssetApproval", x => x.AssetApprovalId);
                    table.ForeignKey(
                        name: "FK_AssetApproval_Asset",
                        column: x => x.AssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetApproval_Reviewer",
                        column: x => x.ReviewedByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_AssetApproval_Uploader",
                        column: x => x.UploadedByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblAssetContentMetadata",
                columns: table => new
                {
                    AssetContentMetadataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MetadataValue = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    SetByUserId = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAssetContentMetadata", x => x.AssetContentMetadataId);
                    table.ForeignKey(
                        name: "FK_ContentMetadata_Asset",
                        column: x => x.AssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentMetadata_User",
                        column: x => x.SetByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblDisplay",
                columns: table => new
                {
                    DisplayId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DisplayMode = table.Column<int>(type: "int", nullable: false),
                    DisplayTypeId = table.Column<int>(type: "int", nullable: false),
                    DisplayGroupId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    DisplayHeight = table.Column<double>(type: "float", nullable: false, defaultValue: 1080.0),
                    DisplayWidth = table.Column<double>(type: "float", nullable: false, defaultValue: 1920.0),
                    ConstraintId = table.Column<int>(type: "int", nullable: false),
                    OverrideAssetId = table.Column<int>(type: "int", nullable: true),
                    UseClock = table.Column<bool>(type: "bit", nullable: false),
                    ClockHeight = table.Column<double>(type: "float", nullable: true),
                    ClockWidth = table.Column<double>(type: "float", nullable: true),
                    UseVideoMask = table.Column<bool>(type: "bit", nullable: false),
                    EnableFade = table.Column<bool>(type: "bit", nullable: false),
                    EnableContentApproval = table.Column<bool>(type: "bit", nullable: false),
                    AssetLibraryLocationId = table.Column<int>(type: "int", nullable: true),
                    StatusSelectorMetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    EndpointId = table.Column<int>(type: "int", nullable: true),
                    EndpointPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 99),
                    TimeZone = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "America/New_York"),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDisplay_1", x => x.DisplayId);
                    table.ForeignKey(
                        name: "FK_tblDisplay_tblAsset",
                        column: x => x.OverrideAssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId");
                    table.ForeignKey(
                        name: "FK_tblDisplay_tblConstraint",
                        column: x => x.ConstraintId,
                        principalTable: "tblConstraint",
                        principalColumn: "ConstraintId");
                    table.ForeignKey(
                        name: "FK_tblDisplay_tblDisplayGroup",
                        column: x => x.DisplayGroupId,
                        principalTable: "tblDisplayGroup",
                        principalColumn: "DisplayGroupId");
                    table.ForeignKey(
                        name: "FK_tblDisplay_tblDisplayType",
                        column: x => x.DisplayTypeId,
                        principalTable: "tblDisplayType",
                        principalColumn: "DisplayTypeId");
                    table.ForeignKey(
                        name: "FK_tblDisplay_tblEndpoint",
                        column: x => x.EndpointId,
                        principalTable: "tblEndpoint",
                        principalColumn: "EndpointId");
                    table.ForeignKey(
                        name: "FK_tblDisplay_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalStage",
                columns: table => new
                {
                    ApprovalStageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovalChainId = table.Column<int>(type: "int", nullable: false),
                    StageOrder = table.Column<int>(type: "int", nullable: false),
                    StageName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    IntraStageOperator = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false, defaultValue: "OR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalStage", x => x.ApprovalStageId);
                    table.ForeignKey(
                        name: "FK_ApprovalStage_Chain",
                        column: x => x.ApprovalChainId,
                        principalTable: "tblApprovalChain",
                        principalColumn: "ApprovalChainId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblAccessSchedule",
                columns: table => new
                {
                    AccessScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationAccessRuleId = table.Column<int>(type: "int", nullable: false),
                    DisplayId = table.Column<int>(type: "int", nullable: true),
                    ConstraintId = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    Sunday = table.Column<bool>(type: "bit", nullable: false),
                    Monday = table.Column<bool>(type: "bit", nullable: false),
                    Tuesday = table.Column<bool>(type: "bit", nullable: false),
                    Wednesday = table.Column<bool>(type: "bit", nullable: false),
                    Thursday = table.Column<bool>(type: "bit", nullable: false),
                    Friday = table.Column<bool>(type: "bit", nullable: false),
                    Saturday = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblAccessSchedule", x => x.AccessScheduleId);
                    table.ForeignKey(
                        name: "FK_AccessSchedule_Constraint",
                        column: x => x.ConstraintId,
                        principalTable: "tblConstraint",
                        principalColumn: "ConstraintId");
                    table.ForeignKey(
                        name: "FK_AccessSchedule_Display",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId");
                    table.ForeignKey(
                        name: "FK_AccessSchedule_Rule",
                        column: x => x.LocationAccessRuleId,
                        principalTable: "tblLocationAccessRule",
                        principalColumn: "LocationAccessRuleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblControlClientDisplay",
                columns: table => new
                {
                    ControlClientDisplayId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ControlClientId = table.Column<int>(type: "int", nullable: false),
                    DisplayId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblControlClientDisplay", x => x.ControlClientDisplayId);
                    table.ForeignKey(
                        name: "FK_ControlClientDisplay_Client",
                        column: x => x.ControlClientId,
                        principalTable: "tblControlClient",
                        principalColumn: "ControlClientId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ControlClientDisplay_Display",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDevice",
                columns: table => new
                {
                    DeviceID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    CustomerID = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    LocationID = table.Column<int>(type: "int", nullable: true),
                    DisplayID = table.Column<int>(type: "int", nullable: true),
                    Manufacturer = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    Model = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    SerialNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    IPAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MACAddress = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    OSVersion = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Heartbeat = table.Column<DateTime>(type: "datetime", nullable: false),
                    StorageType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    StorageSize = table.Column<long>(type: "bigint", nullable: true),
                    StorageHealth = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ClientAppVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    TargetAppVersionId = table.Column<int>(type: "int", nullable: true),
                    LastUpdateAttempt = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastUpdateStatus = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    PlatformId = table.Column<int>(type: "int", nullable: true),
                    AutoHealEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DeviceTypeId = table.Column<int>(type: "int", nullable: true),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDevice", x => x.DeviceID);
                    table.ForeignKey(
                        name: "FK_tblDevice_Platform",
                        column: x => x.PlatformId,
                        principalTable: "tblAppPlatform",
                        principalColumn: "PlatformId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblDevice_TargetAppVersion",
                        column: x => x.TargetAppVersionId,
                        principalTable: "tblAppVersion",
                        principalColumn: "AppVersionId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblDevice_tblDeviceType",
                        column: x => x.DeviceTypeId,
                        principalTable: "tblDeviceType",
                        principalColumn: "DeviceTypeId");
                    table.ForeignKey(
                        name: "FK_tblDevice_tblDisplay",
                        column: x => x.DisplayID,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId");
                    table.ForeignKey(
                        name: "FK_tblDevice_tblLocation",
                        column: x => x.LocationID,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                });

            migrationBuilder.CreateTable(
                name: "tblFloorPlanElement",
                columns: table => new
                {
                    FloorPlanElementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    ChildLocationId = table.Column<int>(type: "int", nullable: true),
                    ChildDisplayId = table.Column<int>(type: "int", nullable: true),
                    PositionTop = table.Column<double>(type: "float", nullable: false),
                    PositionLeft = table.Column<double>(type: "float", nullable: false),
                    ElementWidth = table.Column<double>(type: "float", nullable: false),
                    ElementHeight = table.Column<double>(type: "float", nullable: false),
                    IconPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    IconLabel = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ShowLivePreview = table.Column<bool>(type: "bit", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    Position3DX = table.Column<double>(type: "float", nullable: true),
                    Position3DY = table.Column<double>(type: "float", nullable: true),
                    Position3DZ = table.Column<double>(type: "float", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblFloorPlanElement", x => x.FloorPlanElementId);
                    table.ForeignKey(
                        name: "FK_FloorPlanElement_ChildDisplay",
                        column: x => x.ChildDisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId");
                    table.ForeignKey(
                        name: "FK_FloorPlanElement_ChildLocation",
                        column: x => x.ChildLocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_FloorPlanElement_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblOverrideDisplay",
                columns: table => new
                {
                    OverrideDisplayId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OverrideId = table.Column<int>(type: "int", nullable: false),
                    DisplayId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblOverrideDisplay", x => x.OverrideDisplayId);
                    table.ForeignKey(
                        name: "FK_tblOverrideDisplay_tblDisplay",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblOverrideDisplay_tblOverride",
                        column: x => x.OverrideId,
                        principalTable: "tblOverride",
                        principalColumn: "OverrideId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblScheduleEvent",
                columns: table => new
                {
                    ScheduleEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    ScheduleEventTypeId = table.Column<int>(type: "int", nullable: true),
                    DisplayId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    AirlineCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAssignment = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    DefaultContent = table.Column<bool>(type: "bit", nullable: false),
                    Sunday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Monday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Tuesday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Wednesday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Thursday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Friday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Saturday = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UserGroupId = table.Column<int>(type: "int", nullable: true),
                    OverrideConstraintId = table.Column<int>(type: "int", nullable: true),
                    DefaultAssetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblScheduleEvent", x => x.ScheduleEventId);
                    table.ForeignKey(
                        name: "FK_tblScheduleEvent_OverrideConstraint",
                        column: x => x.OverrideConstraintId,
                        principalTable: "tblConstraint",
                        principalColumn: "ConstraintId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblScheduleEvent_UserGroup",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblScheduleEvent_tblAsset_DefaultAssetId",
                        column: x => x.DefaultAssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId");
                    table.ForeignKey(
                        name: "FK_tblScheduleEvent_tblDisplay",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId");
                });

            migrationBuilder.CreateTable(
                name: "tblWallLayoutMapping",
                columns: table => new
                {
                    WallLayoutMappingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EndpointId = table.Column<int>(type: "int", nullable: false),
                    DisplayId = table.Column<int>(type: "int", nullable: false),
                    LayerRole = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "status"),
                    PlatformData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblWallLayoutMapping", x => x.WallLayoutMappingId);
                    table.ForeignKey(
                        name: "FK_tblWallLayoutMapping_Display",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblWallLayoutMapping_Endpoint",
                        column: x => x.EndpointId,
                        principalTable: "tblEndpoint",
                        principalColumn: "EndpointId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalRequest",
                columns: table => new
                {
                    ApprovalRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetApprovalId = table.Column<int>(type: "int", nullable: false),
                    ApprovalChainId = table.Column<int>(type: "int", nullable: false),
                    CurrentStageId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "pending"),
                    SubmittedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CompletedDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalRequest", x => x.ApprovalRequestId);
                    table.ForeignKey(
                        name: "FK_ApprovalRequest_AssetApproval",
                        column: x => x.AssetApprovalId,
                        principalTable: "tblAssetApproval",
                        principalColumn: "AssetApprovalId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalRequest_Chain",
                        column: x => x.ApprovalChainId,
                        principalTable: "tblApprovalChain",
                        principalColumn: "ApprovalChainId");
                    table.ForeignKey(
                        name: "FK_ApprovalRequest_CurrentStage",
                        column: x => x.CurrentStageId,
                        principalTable: "tblApprovalStage",
                        principalColumn: "ApprovalStageId");
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalStageGroup",
                columns: table => new
                {
                    ApprovalStageGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovalStageId = table.Column<int>(type: "int", nullable: false),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalStageGroup", x => x.ApprovalStageGroupId);
                    table.ForeignKey(
                        name: "FK_ApprovalStageGroup_Group",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId");
                    table.ForeignKey(
                        name: "FK_ApprovalStageGroup_Stage",
                        column: x => x.ApprovalStageId,
                        principalTable: "tblApprovalStage",
                        principalColumn: "ApprovalStageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TblContentChannel",
                columns: table => new
                {
                    ChannelId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LocationID = table.Column<int>(type: "int", nullable: true),
                    DisplayID = table.Column<int>(type: "int", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ConnectorId = table.Column<int>(type: "int", nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    ExtendedData = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TblContentChannel", x => x.ChannelId);
                    table.ForeignKey(
                        name: "FK_TblContentChannel_tblDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TblContentChannel_tblDisplay_DisplayID",
                        column: x => x.DisplayID,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TblContentChannel_tblLocation_LocationID",
                        column: x => x.LocationID,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tblContentHealthEvent",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ContentUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreviousStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EventTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    ReceivedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblContentHealthEvent", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_ContentHealthEvent_Device",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDeviceMetadata",
                columns: table => new
                {
                    DeviceMetadataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MetadataValue = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDeviceMetadata", x => x.DeviceMetadataId);
                    table.ForeignKey(
                        name: "FK_tblDeviceMetadata_tblDevice",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDeviceToken",
                columns: table => new
                {
                    DeviceTokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    TokenHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    TokenPrefix = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    PairingRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDeviceToken", x => x.DeviceTokenId);
                    table.ForeignKey(
                        name: "FK_DeviceToken_ApprovedBy",
                        column: x => x.ApprovedByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_DeviceToken_Device",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tblOverrideContent",
                columns: table => new
                {
                    OverrideContentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OverrideId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ResolutionWidth = table.Column<int>(type: "int", nullable: false),
                    ResolutionHeight = table.Column<int>(type: "int", nullable: false),
                    ContentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    Duration = table.Column<int>(type: "int", nullable: false, defaultValue: 7),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblOverrideContent", x => x.OverrideContentId);
                    table.ForeignKey(
                        name: "FK_tblOverrideContent_tblAsset",
                        column: x => x.AssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblOverrideContent_tblDevice",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tblOverrideContent_tblOverride",
                        column: x => x.OverrideId,
                        principalTable: "tblOverride",
                        principalColumn: "OverrideId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblOverrideDevice",
                columns: table => new
                {
                    OverrideDeviceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OverrideId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblOverrideDevice", x => x.OverrideDeviceId);
                    table.ForeignKey(
                        name: "FK_tblOverrideDevice_tblDevice",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblOverrideDevice_tblOverride",
                        column: x => x.OverrideId,
                        principalTable: "tblOverride",
                        principalColumn: "OverrideId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblUpdateDeploymentDevice",
                columns: table => new
                {
                    DeploymentDeviceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeploymentId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUpdateDeploymentDevice", x => x.DeploymentDeviceId);
                    table.ForeignKey(
                        name: "FK_tblUpdateDeploymentDevice_Deployment",
                        column: x => x.DeploymentId,
                        principalTable: "tblUpdateDeployment",
                        principalColumn: "DeploymentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblUpdateDeploymentDevice_Device",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblUpdateDeploymentProgress",
                columns: table => new
                {
                    ProgressId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeploymentId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    BatchNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    PreviousVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    NewVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUpdateDeploymentProgress", x => x.ProgressId);
                    table.ForeignKey(
                        name: "FK_tblUpdateDeploymentProgress_Deployment",
                        column: x => x.DeploymentId,
                        principalTable: "tblUpdateDeployment",
                        principalColumn: "DeploymentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblUpdateDeploymentProgress_Device",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblFloorPlanIconSchedule",
                columns: table => new
                {
                    FloorPlanIconScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FloorPlanElementId = table.Column<int>(type: "int", nullable: false),
                    AssetLibraryLocationId = table.Column<int>(type: "int", nullable: true),
                    MetadataKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    MetadataValue = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    IconAssetId = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    Sunday = table.Column<bool>(type: "bit", nullable: false),
                    Monday = table.Column<bool>(type: "bit", nullable: false),
                    Tuesday = table.Column<bool>(type: "bit", nullable: false),
                    Wednesday = table.Column<bool>(type: "bit", nullable: false),
                    Thursday = table.Column<bool>(type: "bit", nullable: false),
                    Friday = table.Column<bool>(type: "bit", nullable: false),
                    Saturday = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblFloorPlanIconSchedule", x => x.FloorPlanIconScheduleId);
                    table.ForeignKey(
                        name: "FK_FloorPlanIconSchedule_Asset",
                        column: x => x.IconAssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FloorPlanIconSchedule_AssetLibrary",
                        column: x => x.AssetLibraryLocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_FloorPlanIconSchedule_Element",
                        column: x => x.FloorPlanElementId,
                        principalTable: "tblFloorPlanElement",
                        principalColumn: "FloorPlanElementId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblEventLog",
                columns: table => new
                {
                    RecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventSource = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    EventTypeId = table.Column<int>(type: "int", nullable: false),
                    EventTitle = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    EventMessage = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    EventSeverityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    DisplayId = table.Column<int>(type: "int", nullable: true),
                    ScheduleEventId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: true),
                    TicketCreated = table.Column<bool>(type: "bit", nullable: false),
                    TicketId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblEventLog", x => x.RecordId);
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblAsset",
                        column: x => x.AssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId");
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "tblDevice",
                        principalColumn: "DeviceID");
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblDisplay",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId");
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblEventSeverity",
                        column: x => x.EventSeverityId,
                        principalTable: "tblEventSeverity",
                        principalColumn: "EventSeverityId");
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblEventType",
                        column: x => x.EventTypeId,
                        principalTable: "tblEventType",
                        principalColumn: "EventTypeId");
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblLocation_LocationId",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_tblEventLog_tblScheduleEvent",
                        column: x => x.ScheduleEventId,
                        principalTable: "tblScheduleEvent",
                        principalColumn: "ScheduleEventId");
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalAction",
                columns: table => new
                {
                    ApprovalActionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovalRequestId = table.Column<int>(type: "int", nullable: false),
                    ApprovalStageId = table.Column<int>(type: "int", nullable: false),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    ActingUserId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ActionDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalAction", x => x.ApprovalActionId);
                    table.ForeignKey(
                        name: "FK_ApprovalAction_Group",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId");
                    table.ForeignKey(
                        name: "FK_ApprovalAction_Request",
                        column: x => x.ApprovalRequestId,
                        principalTable: "tblApprovalRequest",
                        principalColumn: "ApprovalRequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalAction_Stage",
                        column: x => x.ApprovalStageId,
                        principalTable: "tblApprovalStage",
                        principalColumn: "ApprovalStageId");
                    table.ForeignKey(
                        name: "FK_ApprovalAction_User",
                        column: x => x.ActingUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalComment",
                columns: table => new
                {
                    ApprovalCommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovalRequestId = table.Column<int>(type: "int", nullable: true),
                    AssetApprovalId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CommentText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CommentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalComment", x => x.ApprovalCommentId);
                    table.ForeignKey(
                        name: "FK_ApprovalComment_AssetApproval",
                        column: x => x.AssetApprovalId,
                        principalTable: "tblAssetApproval",
                        principalColumn: "AssetApprovalId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalComment_Request",
                        column: x => x.ApprovalRequestId,
                        principalTable: "tblApprovalRequest",
                        principalColumn: "ApprovalRequestId");
                    table.ForeignKey(
                        name: "FK_ApprovalComment_User",
                        column: x => x.UserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "tblApprovalQueueEntry",
                columns: table => new
                {
                    QueueEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovalRequestId = table.Column<int>(type: "int", nullable: false),
                    ApprovalStageId = table.Column<int>(type: "int", nullable: false),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "pending"),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ResolvedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApprovalQueueEntry", x => x.QueueEntryId);
                    table.ForeignKey(
                        name: "FK_QueueEntry_Group",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId");
                    table.ForeignKey(
                        name: "FK_QueueEntry_Request",
                        column: x => x.ApprovalRequestId,
                        principalTable: "tblApprovalRequest",
                        principalColumn: "ApprovalRequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueueEntry_ResolvedBy",
                        column: x => x.ResolvedByUserId,
                        principalTable: "tblUser",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_QueueEntry_Stage",
                        column: x => x.ApprovalStageId,
                        principalTable: "tblApprovalStage",
                        principalColumn: "ApprovalStageId");
                });

            migrationBuilder.CreateTable(
                name: "TblContentChannelMetadata",
                columns: table => new
                {
                    MetadataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    MetadataKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MetadataValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TblContentChannelMetadata", x => x.MetadataId);
                    table.ForeignKey(
                        name: "FK_TblContentChannelMetadata_TblContentChannel_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "TblContentChannel",
                        principalColumn: "ChannelId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblAccessSchedule_ConstraintId",
                table: "tblAccessSchedule",
                column: "ConstraintId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAccessSchedule_DisplayId",
                table: "tblAccessSchedule",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAccessSchedule_LocationAccessRuleId",
                table: "tblAccessSchedule",
                column: "LocationAccessRuleId");

            migrationBuilder.CreateIndex(
                name: "UX_tblAccessSchedule_ContentPackageId",
                table: "tblAccessSchedule",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblAirlineLocation_LocationId",
                table: "tblAirlineLocation",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApiKey_ApiKeyHash",
                table: "tblApiKey",
                column: "ApiKeyHash",
                unique: true,
                filter: "[ApiKeyHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tblApiKey_CreatedBy",
                table: "tblApiKey",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_tblApiKey_LocationId",
                table: "tblApiKey",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAppPlatform_FolderName",
                table: "tblAppPlatform",
                column: "FolderName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblAppPlatform_PlatformName",
                table: "tblAppPlatform",
                column: "PlatformName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalAction_ActingUserId",
                table: "tblApprovalAction",
                column: "ActingUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalAction_ApprovalRequestId",
                table: "tblApprovalAction",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalAction_ApprovalStageId",
                table: "tblApprovalAction",
                column: "ApprovalStageId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalAction_UserGroupId",
                table: "tblApprovalAction",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalChain_LocationAccessRuleId",
                table: "tblApprovalChain",
                column: "LocationAccessRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalChain_LocationId",
                table: "tblApprovalChain",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_tblApprovalChain_ContentPackageId",
                table: "tblApprovalChain",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalComment_AssetApprovalId",
                table: "tblApprovalComment",
                column: "AssetApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalComment_ApprovalRequestId",
                table: "tblApprovalComment",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalComment_UserId",
                table: "tblApprovalComment",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntry_RequestStatus",
                table: "tblApprovalQueueEntry",
                columns: new[] { "ApprovalRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalQueueEntry_ApprovalStageId",
                table: "tblApprovalQueueEntry",
                column: "ApprovalStageId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalQueueEntry_ResolvedByUserId",
                table: "tblApprovalQueueEntry",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalQueueEntry_UserGroupId",
                table: "tblApprovalQueueEntry",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_Status",
                table: "tblApprovalRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalRequest_ApprovalChainId",
                table: "tblApprovalRequest",
                column: "ApprovalChainId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalRequest_AssetApprovalId",
                table: "tblApprovalRequest",
                column: "AssetApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalRequest_CurrentStageId",
                table: "tblApprovalRequest",
                column: "CurrentStageId");

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalStage_ApprovalChainId_StageOrder",
                table: "tblApprovalStage",
                columns: new[] { "ApprovalChainId", "StageOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalStageGroup_ApprovalStageId_UserGroupId",
                table: "tblApprovalStageGroup",
                columns: new[] { "ApprovalStageId", "UserGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblApprovalStageGroup_UserGroupId",
                table: "tblApprovalStageGroup",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAppVersion_PlatformId",
                table: "tblAppVersion",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAppVersion_PlatformId_VersionNumber",
                table: "tblAppVersion",
                columns: new[] { "PlatformId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblAsset_AssetGroupId",
                table: "tblAsset",
                column: "AssetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAsset_AssetTypeId",
                table: "tblAsset",
                column: "AssetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAsset_ConstraintId",
                table: "tblAsset",
                column: "ConstraintId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAsset_LocationId",
                table: "tblAsset",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_tblAsset_ContentPackageId",
                table: "tblAsset",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetApproval_Status",
                table: "tblAssetApproval",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_tblAssetApproval_AssetId",
                table: "tblAssetApproval",
                column: "AssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblAssetApproval_ReviewedByUserId",
                table: "tblAssetApproval",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAssetApproval_UploadedByUserId",
                table: "tblAssetApproval",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentMetadata_AssetKey",
                table: "tblAssetContentMetadata",
                columns: new[] { "AssetId", "MetadataKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblAssetContentMetadata_SetByUserId",
                table: "tblAssetContentMetadata",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_tblAssetContentMetadata_ContentPackageId",
                table: "tblAssetContentMetadata",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_tblAssetGroup_ContentPackageId",
                table: "tblAssetGroup",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblConstraint_OwningLocationId",
                table: "tblConstraint",
                column: "OwningLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_tblConstraint_ContentPackageId",
                table: "tblConstraint",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TblContentChannel_DeviceId",
                table: "TblContentChannel",
                column: "DeviceId",
                unique: true,
                filter: "[DeviceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TblContentChannel_DisplayID",
                table: "TblContentChannel",
                column: "DisplayID");

            migrationBuilder.CreateIndex(
                name: "IX_TblContentChannel_LocationID",
                table: "TblContentChannel",
                column: "LocationID");

            migrationBuilder.CreateIndex(
                name: "IX_TblContentChannelMetadata_ChannelId_MetadataKey",
                table: "TblContentChannelMetadata",
                columns: new[] { "ChannelId", "MetadataKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentHealthEvent_DeviceId",
                table: "tblContentHealthEvent",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentHealthEvent_EventTime",
                table: "tblContentHealthEvent",
                column: "EventTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_tblContentPackageHistory_StartedAt",
                table: "tblContentPackageHistory",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tblContentPackageHistoryItem_ContentPackageHistoryId",
                table: "tblContentPackageHistoryItem",
                column: "ContentPackageHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_tblContentPackageHistoryItem_ContentPackageId",
                table: "tblContentPackageHistoryItem",
                column: "ContentPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClient_CreatedBy",
                table: "tblControlClient",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClient_LocationId",
                table: "tblControlClient",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientDisplay_DisplayId",
                table: "tblControlClientDisplay",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "UQ_ControlClientDisplay",
                table: "tblControlClientDisplay",
                columns: new[] { "ControlClientId", "DisplayId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientPairingRequest_CodeHash",
                table: "tblControlClientPairingRequest",
                column: "PairingCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientPairingRequest_ControlClientId",
                table: "tblControlClientPairingRequest",
                column: "ControlClientId");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientPairingRequest_ResolvedByUserId",
                table: "tblControlClientPairingRequest",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientPairingRequest_Status",
                table: "tblControlClientPairingRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientToken_ControlClientId",
                table: "tblControlClientToken",
                column: "ControlClientId");

            migrationBuilder.CreateIndex(
                name: "IX_tblControlClientToken_TokenHash",
                table: "tblControlClientToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_DeviceTypeId",
                table: "tblDevice",
                column: "DeviceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_DisplayID",
                table: "tblDevice",
                column: "DisplayID");

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_LocationID",
                table: "tblDevice",
                column: "LocationID");

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_PlatformId",
                table: "tblDevice",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_TargetAppVersionId",
                table: "tblDevice",
                column: "TargetAppVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMetadata_DeviceId",
                table: "tblDeviceMetadata",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "UQ_DeviceMetadata",
                table: "tblDeviceMetadata",
                columns: new[] { "DeviceId", "MetadataKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDevicePairingRequest_CodeHash",
                table: "tblDevicePairingRequest",
                column: "PairingCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_tblDevicePairingRequest_ResolvedByUserId",
                table: "tblDevicePairingRequest",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDevicePairingRequest_Status",
                table: "tblDevicePairingRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tblDeviceToken_ApprovedByUserId",
                table: "tblDeviceToken",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDeviceToken_DeviceId",
                table: "tblDeviceToken",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDeviceToken_TokenHash",
                table: "tblDeviceToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDiscoveredAdGroup_AdoptedUserGroupId",
                table: "tblDiscoveredAdGroup",
                column: "AdoptedUserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDiscoveredAdGroup_ObjectId",
                table: "tblDiscoveredAdGroup",
                column: "ObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDisplay_ConstraintId",
                table: "tblDisplay",
                column: "ConstraintId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDisplay_DisplayGroupId",
                table: "tblDisplay",
                column: "DisplayGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDisplay_DisplayTypeId",
                table: "tblDisplay",
                column: "DisplayTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDisplay_EndpointId",
                table: "tblDisplay",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDisplay_LocationId",
                table: "tblDisplay",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDisplay_OverrideAssetId",
                table: "tblDisplay",
                column: "OverrideAssetId");

            migrationBuilder.CreateIndex(
                name: "UX_tblDisplay_ContentPackageId",
                table: "tblDisplay",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_tblDisplayGroup_ContentPackageId",
                table: "tblDisplayGroup",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_AssetId",
                table: "tblEventLog",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_DeviceId",
                table: "tblEventLog",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_DisplayId",
                table: "tblEventLog",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_EventSeverityId",
                table: "tblEventLog",
                column: "EventSeverityId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_EventTypeId",
                table: "tblEventLog",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_LocationId",
                table: "tblEventLog",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLog_ScheduleEventId",
                table: "tblEventLog",
                column: "ScheduleEventId");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLogArchive_ArchivedDate",
                table: "tblEventLogArchive",
                column: "ArchivedDate");

            migrationBuilder.CreateIndex(
                name: "IX_tblEventLogArchive_CreateDate",
                table: "tblEventLogArchive",
                column: "CreateDate");

            migrationBuilder.CreateIndex(
                name: "IX_tblFloorPlanElement_ChildDisplayId",
                table: "tblFloorPlanElement",
                column: "ChildDisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblFloorPlanElement_ChildLocationId",
                table: "tblFloorPlanElement",
                column: "ChildLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblFloorPlanElement_LocationId",
                table: "tblFloorPlanElement",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_tblFloorPlanElement_ContentPackageId",
                table: "tblFloorPlanElement",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblFloorPlanIconSchedule_AssetLibraryLocationId",
                table: "tblFloorPlanIconSchedule",
                column: "AssetLibraryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblFloorPlanIconSchedule_FloorPlanElementId",
                table: "tblFloorPlanIconSchedule",
                column: "FloorPlanElementId");

            migrationBuilder.CreateIndex(
                name: "IX_tblFloorPlanIconSchedule_IconAssetId",
                table: "tblFloorPlanIconSchedule",
                column: "IconAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalAccount_Email",
                table: "tblLocalAccount",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblLocalAccount_UserId",
                table: "tblLocalAccount",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocation_LocationGroupId",
                table: "tblLocation",
                column: "LocationGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocation_LocationTypeId",
                table: "tblLocation",
                column: "LocationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocation_OwningUserGroupId",
                table: "tblLocation",
                column: "OwningUserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocation_ParentVideoWallLocationId",
                table: "tblLocation",
                column: "ParentVideoWallLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_tblLocation_ContentPackageId",
                table: "tblLocation",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessRule_Unique_WithLevel",
                table: "tblLocationAccessRule",
                columns: new[] { "LocationId", "UserId", "UserGroupId", "AccessLevel" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [UserGroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocationAccessRule_UserGroupId",
                table: "tblLocationAccessRule",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocationAccessRule_UserId",
                table: "tblLocationAccessRule",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_tblLocationAccessRule_ContentPackageId",
                table: "tblLocationAccessRule",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_tblLocationGroup_ContentPackageId",
                table: "tblLocationGroup",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notification_UserRead",
                table: "tblNotification",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideContent_AssetId",
                table: "tblOverrideContent",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideContent_DeviceId",
                table: "tblOverrideContent",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideContent_OverrideId",
                table: "tblOverrideContent",
                column: "OverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideContent_Resolution",
                table: "tblOverrideContent",
                columns: new[] { "ResolutionWidth", "ResolutionHeight" });

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideDevice_DeviceId",
                table: "tblOverrideDevice",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideDevice_OverrideId",
                table: "tblOverrideDevice",
                column: "OverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideDevice_OverrideId_DeviceId",
                table: "tblOverrideDevice",
                columns: new[] { "OverrideId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideDisplay_DisplayId",
                table: "tblOverrideDisplay",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideDisplay_OverrideId",
                table: "tblOverrideDisplay",
                column: "OverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_tblOverrideDisplay_OverrideId_DisplayId",
                table: "tblOverrideDisplay",
                columns: new[] { "OverrideId", "DisplayId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblParameter_KeyName",
                table: "tblParameter",
                columns: new[] { "KeyName", "CustomerID" },
                unique: true,
                filter: "[CustomerID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tblScheduleEvent_DefaultAssetId",
                table: "tblScheduleEvent",
                column: "DefaultAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblScheduleEvent_DisplayId",
                table: "tblScheduleEvent",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblScheduleEvent_OverrideConstraintId",
                table: "tblScheduleEvent",
                column: "OverrideConstraintId");

            migrationBuilder.CreateIndex(
                name: "IX_tblScheduleEvent_UserGroupId",
                table: "tblScheduleEvent",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "UX_tblScheduleEvent_ContentPackageId",
                table: "tblScheduleEvent",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeployment_AppVersionId",
                table: "tblUpdateDeployment",
                column: "AppVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeployment_LocationId",
                table: "tblUpdateDeployment",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeploymentDevice_DeploymentId_DeviceId",
                table: "tblUpdateDeploymentDevice",
                columns: new[] { "DeploymentId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeploymentDevice_DeviceId",
                table: "tblUpdateDeploymentDevice",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeploymentProgress_DeploymentId_BatchNumber",
                table: "tblUpdateDeploymentProgress",
                columns: new[] { "DeploymentId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeploymentProgress_DeploymentId_DeviceId",
                table: "tblUpdateDeploymentProgress",
                columns: new[] { "DeploymentId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUpdateDeploymentProgress_DeviceId",
                table: "tblUpdateDeploymentProgress",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUser_RoleId",
                table: "tblUser",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUserGroup_GroupName",
                table: "tblUserGroup",
                column: "GroupName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUserGroupLocationDefault_DefaultConstraintId",
                table: "tblUserGroupLocationDefault",
                column: "DefaultConstraintId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUserGroupLocationDefault_LocationId",
                table: "tblUserGroupLocationDefault",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_tblUserGroupLocationDefault_GroupLocation",
                table: "tblUserGroupLocationDefault",
                columns: new[] { "UserGroupId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUserGroupMembership_UserGroupId_UserId",
                table: "tblUserGroupMembership",
                columns: new[] { "UserGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUserGroupMembership_UserId",
                table: "tblUserGroupMembership",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMetadata_UserGroupId",
                table: "tblUserGroupMetadata",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "UQ_UserGroupMetadata",
                table: "tblUserGroupMetadata",
                columns: new[] { "UserGroupId", "MetadataKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUserLocation_LocationId",
                table: "tblUserLocation",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblUserLocation_UserId",
                table: "tblUserLocation",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMetadata_UserId",
                table: "tblUserMetadata",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_UserMetadata",
                table: "tblUserMetadata",
                columns: new[] { "UserId", "MetadataKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblUserNotificationPreference_UserId",
                table: "tblUserNotificationPreference",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_tblWallLayoutMapping_Display",
                table: "tblWallLayoutMapping",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblWallLayoutMapping_Endpoint",
                table: "tblWallLayoutMapping",
                column: "EndpointId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblAccessSchedule");

            migrationBuilder.DropTable(
                name: "tblAirlineLocation");

            migrationBuilder.DropTable(
                name: "tblApiKey");

            migrationBuilder.DropTable(
                name: "tblApprovalAction");

            migrationBuilder.DropTable(
                name: "tblApprovalComment");

            migrationBuilder.DropTable(
                name: "tblApprovalQueueEntry");

            migrationBuilder.DropTable(
                name: "tblApprovalStageGroup");

            migrationBuilder.DropTable(
                name: "tblAssetContentMetadata");

            migrationBuilder.DropTable(
                name: "tblCloudPlatformConnector");

            migrationBuilder.DropTable(
                name: "TblContentChannelMetadata");

            migrationBuilder.DropTable(
                name: "tblContentHealthEvent");

            migrationBuilder.DropTable(
                name: "tblContentPackageEnvironmentLabel");

            migrationBuilder.DropTable(
                name: "tblContentPackageHistoryItem");

            migrationBuilder.DropTable(
                name: "tblControlClientDisplay");

            migrationBuilder.DropTable(
                name: "tblControlClientPairingRequest");

            migrationBuilder.DropTable(
                name: "tblControlClientToken");

            migrationBuilder.DropTable(
                name: "tblDeviceMetadata");

            migrationBuilder.DropTable(
                name: "tblDeviceModelConfig");

            migrationBuilder.DropTable(
                name: "tblDevicePairingRequest");

            migrationBuilder.DropTable(
                name: "tblDeviceToken");

            migrationBuilder.DropTable(
                name: "tblDiscoveredAdGroup");

            migrationBuilder.DropTable(
                name: "tblEventLog");

            migrationBuilder.DropTable(
                name: "tblEventLogArchive");

            migrationBuilder.DropTable(
                name: "tblFloorPlanIconSchedule");

            migrationBuilder.DropTable(
                name: "tblLlmInteraction");

            migrationBuilder.DropTable(
                name: "tblLlmPromptConfig");

            migrationBuilder.DropTable(
                name: "tblLlmProviderConfig");

            migrationBuilder.DropTable(
                name: "tblLocalAccount");

            migrationBuilder.DropTable(
                name: "tblMetadataDefinition");

            migrationBuilder.DropTable(
                name: "tblNotification");

            migrationBuilder.DropTable(
                name: "tblOverrideContent");

            migrationBuilder.DropTable(
                name: "tblOverrideDevice");

            migrationBuilder.DropTable(
                name: "tblOverrideDisplay");

            migrationBuilder.DropTable(
                name: "tblParameter");

            migrationBuilder.DropTable(
                name: "tblSystemSetting");

            migrationBuilder.DropTable(
                name: "tblUpdateDeploymentDevice");

            migrationBuilder.DropTable(
                name: "tblUpdateDeploymentProgress");

            migrationBuilder.DropTable(
                name: "tblUserGroupLocationDefault");

            migrationBuilder.DropTable(
                name: "tblUserGroupMembership");

            migrationBuilder.DropTable(
                name: "tblUserGroupMetadata");

            migrationBuilder.DropTable(
                name: "tblUserLocation");

            migrationBuilder.DropTable(
                name: "tblUserMetadata");

            migrationBuilder.DropTable(
                name: "tblUserNotificationPreference");

            migrationBuilder.DropTable(
                name: "tblWallLayoutMapping");

            migrationBuilder.DropTable(
                name: "tblApprovalRequest");

            migrationBuilder.DropTable(
                name: "TblContentChannel");

            migrationBuilder.DropTable(
                name: "tblContentPackageHistory");

            migrationBuilder.DropTable(
                name: "tblControlClient");

            migrationBuilder.DropTable(
                name: "tblEventSeverity");

            migrationBuilder.DropTable(
                name: "tblEventType");

            migrationBuilder.DropTable(
                name: "tblScheduleEvent");

            migrationBuilder.DropTable(
                name: "tblFloorPlanElement");

            migrationBuilder.DropTable(
                name: "tblOverride");

            migrationBuilder.DropTable(
                name: "tblUpdateDeployment");

            migrationBuilder.DropTable(
                name: "tblAssetApproval");

            migrationBuilder.DropTable(
                name: "tblApprovalStage");

            migrationBuilder.DropTable(
                name: "tblDevice");

            migrationBuilder.DropTable(
                name: "tblApprovalChain");

            migrationBuilder.DropTable(
                name: "tblAppVersion");

            migrationBuilder.DropTable(
                name: "tblDeviceType");

            migrationBuilder.DropTable(
                name: "tblDisplay");

            migrationBuilder.DropTable(
                name: "tblLocationAccessRule");

            migrationBuilder.DropTable(
                name: "tblAppPlatform");

            migrationBuilder.DropTable(
                name: "tblAsset");

            migrationBuilder.DropTable(
                name: "tblDisplayGroup");

            migrationBuilder.DropTable(
                name: "tblDisplayType");

            migrationBuilder.DropTable(
                name: "tblEndpoint");

            migrationBuilder.DropTable(
                name: "tblUser");

            migrationBuilder.DropTable(
                name: "tblAssetGroup");

            migrationBuilder.DropTable(
                name: "tblAssetType");

            migrationBuilder.DropTable(
                name: "tblConstraint");

            migrationBuilder.DropTable(
                name: "tblUserRole");

            migrationBuilder.DropTable(
                name: "tblLocation");

            migrationBuilder.DropTable(
                name: "tblUserGroup");

            migrationBuilder.DropTable(
                name: "tblLocationGroup");

            migrationBuilder.DropTable(
                name: "tblLocationType");
        }
    }
}
