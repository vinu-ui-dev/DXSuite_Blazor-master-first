using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblLocationBundle",
                columns: table => new
                {
                    LocationBundleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BundleKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CurrentVersion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationBundle", x => x.LocationBundleId);
                });

            migrationBuilder.CreateTable(
                name: "tblLocationBundleHistory",
                columns: table => new
                {
                    LocationBundleHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BundleKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    Action = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    InitiatedByUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationBundleHistory", x => x.LocationBundleHistoryId);
                });

            migrationBuilder.CreateTable(
                name: "tblLocationBundleStorage",
                columns: table => new
                {
                    LocationBundleStorageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastEditUser = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationBundleStorage", x => x.LocationBundleStorageId);
                    table.ForeignKey(
                        name: "FK_tblLocationBundleStorage_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblLocationBundleVersion",
                columns: table => new
                {
                    LocationBundleVersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationBundleId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    EntryPath = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UploadDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationBundleVersion", x => x.LocationBundleVersionId);
                    table.ForeignKey(
                        name: "FK_tblLocationBundleVersion_tblLocationBundle",
                        column: x => x.LocationBundleId,
                        principalTable: "tblLocationBundle",
                        principalColumn: "LocationBundleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblLocationBundle_BundleKey",
                table: "tblLocationBundle",
                column: "BundleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblLocationBundleHistory_StartedAt",
                table: "tblLocationBundleHistory",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tblLocationBundleStorage_LocationId_StorageKey",
                table: "tblLocationBundleStorage",
                columns: new[] { "LocationId", "StorageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblLocationBundleVersion_LocationBundleId_Version",
                table: "tblLocationBundleVersion",
                columns: new[] { "LocationBundleId", "Version" },
                unique: true);

            // Seed the two bundle-hosted location types. LocationTypeId is identity and
            // differs per environment, so the app recognizes these rows by the
            // ComponentTypeName sentinel "bundle:host" (LocationBundleConstants), never by id.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeName = 'Custom' AND ComponentTypeName = 'bundle:host')
INSERT INTO tblLocationType (LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection, CreateDate)
VALUES ('Custom', 0, 1, 'bundle:host', 0, GETDATE());

IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeName = 'Custom Display' AND ComponentTypeName = 'bundle:host')
INSERT INTO tblLocationType (LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection, CreateDate)
VALUES ('Custom Display', 1, 1, 'bundle:host', 1, GETDATE());
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the seeded types only when no location references them — a Down
            // must not orphan existing locations.
            migrationBuilder.Sql(@"
DELETE FROM tblLocationType
WHERE ComponentTypeName = 'bundle:host'
  AND NOT EXISTS (SELECT 1 FROM tblLocation WHERE tblLocation.LocationTypeId = tblLocationType.LocationTypeId);
");

            migrationBuilder.DropTable(
                name: "tblLocationBundleHistory");

            migrationBuilder.DropTable(
                name: "tblLocationBundleStorage");

            migrationBuilder.DropTable(
                name: "tblLocationBundleVersion");

            migrationBuilder.DropTable(
                name: "tblLocationBundle");
        }
    }
}
