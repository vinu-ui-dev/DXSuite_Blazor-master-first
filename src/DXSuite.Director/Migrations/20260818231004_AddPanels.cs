using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddPanels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblPanel",
                columns: table => new
                {
                    PanelId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PanelKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PanelName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    AssetLibraryLocationId = table.Column<int>(type: "int", nullable: true),
                    BundleKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PinnedVersion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    HotspotMapJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastSeenIp = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPanel", x => x.PanelId);
                    table.ForeignKey(
                        name: "FK_tblPanel_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblPanel_LocationId",
                table: "tblPanel",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblPanel_PanelKey",
                table: "tblPanel",
                column: "PanelKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblPanel");
        }
    }
}
