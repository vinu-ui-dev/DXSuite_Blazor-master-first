using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Sentinel.Migrations
{
    /// <inheritdoc />
    public partial class AddPulseReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentinelPulseDeviceState",
                columns: table => new
                {
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    PulseCustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PulseDeviceID = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    LastHeartbeatSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSuccessUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    SuppressionReason = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkBrokenUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelPulseDeviceState", x => x.DeviceInstanceID);
                    table.ForeignKey(
                        name: "FK_SentinelPulseDeviceState_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentinelPulseIssueLevel",
                columns: table => new
                {
                    SentinelPulseIssueLevelID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    MonitorPointId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Severity = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    DirectorAlertKind = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    ErrorLevel = table.Column<byte>(type: "tinyint", nullable: true),
                    MonitorItemID = table.Column<int>(type: "int", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelPulseIssueLevel", x => x.SentinelPulseIssueLevelID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelPulseItemState",
                columns: table => new
                {
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    MonitorItemID = table.Column<int>(type: "int", nullable: false),
                    ErrorLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    OpenIssueCodes = table.Column<string>(type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: false),
                    Summary = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    LastPublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelPulseItemState", x => new { x.DeviceInstanceID, x.MonitorItemID });
                    table.ForeignKey(
                        name: "FK_SentinelPulseItemState_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelPulseIssueLevel_Kind_MonitorPointId_Severity_DirectorAlertKind",
                table: "SentinelPulseIssueLevel",
                columns: new[] { "Kind", "MonitorPointId", "Severity", "DirectorAlertKind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentinelPulseDeviceState");

            migrationBuilder.DropTable(
                name: "SentinelPulseIssueLevel");

            migrationBuilder.DropTable(
                name: "SentinelPulseItemState");
        }
    }
}
