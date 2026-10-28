using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorUrlMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblDirectorUrl",
                columns: table => new
                {
                    DirectorUrlId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MonitorId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UrlKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    LinkedDeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsMonitored = table.Column<bool>(type: "bit", nullable: false),
                    ExpectedIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenIp = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    AlertState = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    LastTransitionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDirectorUrl", x => x.DirectorUrlId);
                });

            migrationBuilder.CreateIndex(
                name: "UX_tblDirectorUrl_MonitorUrl",
                table: "tblDirectorUrl",
                columns: new[] { "MonitorId", "UrlKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblDirectorUrl");
        }
    }
}
