using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Sentinel.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceSourceState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentinelDeviceCorrelationSuggestion",
                columns: table => new
                {
                    SuggestionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PrimaryDeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    CandidateDeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelDeviceCorrelationSuggestion", x => x.SuggestionID);
                });

            migrationBuilder.CreateTable(
                name: "SentinelDeviceSourceState",
                columns: table => new
                {
                    DeviceSourceStateID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceInstanceID = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SourceKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    OverallHealth = table.Column<int>(type: "int", nullable: false),
                    LastStateJson = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime", nullable: false),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelDeviceSourceState", x => x.DeviceSourceStateID);
                    table.ForeignKey(
                        name: "FK_SentinelDeviceSourceState_SentinelDeviceInstance_DeviceInstanceID",
                        column: x => x.DeviceInstanceID,
                        principalTable: "SentinelDeviceInstance",
                        principalColumn: "DeviceInstanceID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceCorrelationSuggestion_CustomerID_Status",
                table: "SentinelDeviceCorrelationSuggestion",
                columns: new[] { "CustomerID", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceSourceState_CustomerID",
                table: "SentinelDeviceSourceState",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelDeviceSourceState_DeviceInstanceID_SourceType_SourceKey",
                table: "SentinelDeviceSourceState",
                columns: new[] { "DeviceInstanceID", "SourceType", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentinelDeviceCorrelationSuggestion");

            migrationBuilder.DropTable(
                name: "SentinelDeviceSourceState");
        }
    }
}
