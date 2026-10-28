using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Moves a location's participating metadata keys out of <c>ExtendedData.customMetadataFields</c>
    /// into <c>tblLocationMetadataRule</c>, and adds the two location-level targeting settings.
    ///
    /// The manifest path reads the rules on every poll, and <c>ExtendedData</c> is written by
    /// read-modify-write of the whole blob, so keeping them there would be a lost-update hazard. The
    /// JSON property is stripped in the same migration so no stale second copy is left for a later
    /// reader to trust.
    ///
    /// Blobs are written camelCase but read case-insensitively, so both spellings of the array and of
    /// its <c>name</c> property are folded in.
    /// </summary>
    public partial class AddLocationMetadataRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataMatchMode",
                table: "tblLocation",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "all");

            migrationBuilder.AddColumn<bool>(
                name: "TargetContentByMetadata",
                table: "tblLocation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tblLocationMetadataRule",
                columns: table => new
                {
                    LocationMetadataRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    MetadataKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    UntaggedContentPolicy = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "all"),
                    UntaggedPlayerPolicy = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "all"),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    LastEditDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastEditUser = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblLocationMetadataRule", x => x.LocationMetadataRuleId);
                    table.ForeignKey(
                        name: "FK_LocationMetadataRule_Location",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_tblLocationMetadataRule_ContentPackageId",
                table: "tblLocationMetadataRule",
                column: "ContentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_tblLocationMetadataRule_LocationKey",
                table: "tblLocationMetadataRule",
                columns: new[] { "LocationId", "MetadataKey" },
                unique: true);

            // Deferred name resolution is the only thing letting a batch insert into a table created
            // earlier in it, which is too subtle to rely on silently; EXEC makes it explicit.
            migrationBuilder.Sql(@"EXEC('
INSERT INTO tblLocationMetadataRule (LocationId, MetadataKey, LastEditUser)
SELECT DISTINCT l.LocationId, LTRIM(RTRIM(COALESCE(JSON_VALUE(f.value, ''$.name''), JSON_VALUE(f.value, ''$.Name'')))), ''migration''
FROM tblLocation l
CROSS APPLY OPENJSON(l.ExtendedData) root
CROSS APPLY OPENJSON(root.value) f
WHERE ISJSON(l.ExtendedData) = 1
  AND root.[key] COLLATE Latin1_General_BIN IN (''customMetadataFields'', ''CustomMetadataFields'')
  AND root.type = 4
  AND LTRIM(RTRIM(COALESCE(JSON_VALUE(f.value, ''$.name''), JSON_VALUE(f.value, ''$.Name''), ''''))) <> '''';

UPDATE tblLocation
SET ExtendedData = JSON_MODIFY(JSON_MODIFY(ExtendedData, ''$.customMetadataFields'', NULL), ''$.CustomMetadataFields'', NULL)
WHERE ISJSON(ExtendedData) = 1
  AND (JSON_QUERY(ExtendedData, ''$.customMetadataFields'') IS NOT NULL
    OR JSON_QUERY(ExtendedData, ''$.CustomMetadataFields'') IS NOT NULL);
')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort: the keys go back into the blob in camelCase, and any policy or match mode
            // authored since is lost with the table.
            migrationBuilder.Sql(@"
UPDATE l
SET ExtendedData = JSON_MODIFY(ISNULL(NULLIF(l.ExtendedData, ''), '{}'), '$.customMetadataFields', JSON_QUERY(r.Fields))
FROM tblLocation l
CROSS APPLY (
    SELECT MetadataKey AS name
    FROM tblLocationMetadataRule
    WHERE LocationId = l.LocationId
    ORDER BY MetadataKey
    FOR JSON PATH
) r(Fields)
WHERE r.Fields IS NOT NULL
  AND (l.ExtendedData IS NULL OR l.ExtendedData = '' OR ISJSON(l.ExtendedData) = 1);");

            migrationBuilder.DropTable(
                name: "tblLocationMetadataRule");

            migrationBuilder.DropColumn(
                name: "MetadataMatchMode",
                table: "tblLocation");

            migrationBuilder.DropColumn(
                name: "TargetContentByMetadata",
                table: "tblLocation");
        }
    }
}
