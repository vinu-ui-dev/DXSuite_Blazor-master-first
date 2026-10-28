using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddBrightSignArtifactKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientBootloaderVersion",
                table: "tblDevice",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetBootloaderVersionId",
                table: "tblDevice",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactKind",
                table: "tblAppVersion",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "App");

            migrationBuilder.AddColumn<string>(
                name: "MinBootloaderVersion",
                table: "tblAppVersion",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_TargetBootloaderVersionId",
                table: "tblDevice",
                column: "TargetBootloaderVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_tblDevice_tblAppVersion_TargetBootloaderVersionId",
                table: "tblDevice",
                column: "TargetBootloaderVersionId",
                principalTable: "tblAppVersion",
                principalColumn: "AppVersionId");

            // Seeded here because the heartbeat's platform auto-creation loop is gone, and it was
            // the only mechanism that ever created a tblAppPlatform row: PlayerUpdates.razor reads
            // platforms and never creates one, and there is no admin screen for it.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformName = 'EngageBrightSign')
BEGIN
    INSERT INTO tblAppPlatform (PlatformName, PlatformDescription, FolderName, IsActive, CreateDate)
    VALUES ('EngageBrightSign', 'Engage for BrightSign, native player flavour', 'EngageBrightSign', 1, GETUTCDATE());
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tblDevice_tblAppVersion_TargetBootloaderVersionId",
                table: "tblDevice");

            migrationBuilder.DropIndex(
                name: "IX_tblDevice_TargetBootloaderVersionId",
                table: "tblDevice");

            migrationBuilder.DropColumn(
                name: "ClientBootloaderVersion",
                table: "tblDevice");

            migrationBuilder.DropColumn(
                name: "TargetBootloaderVersionId",
                table: "tblDevice");

            migrationBuilder.DropColumn(
                name: "ArtifactKind",
                table: "tblAppVersion");

            migrationBuilder.DropColumn(
                name: "MinBootloaderVersion",
                table: "tblAppVersion");

            // Only removed when no version or device row still points at it, so a down-migration
            // never orphans an enrolled player or a published artifact.
            migrationBuilder.Sql(@"
DELETE FROM tblAppPlatform
WHERE PlatformName = 'EngageBrightSign'
  AND NOT EXISTS (SELECT 1 FROM tblAppVersion v WHERE v.PlatformId = tblAppPlatform.PlatformId)
  AND NOT EXISTS (SELECT 1 FROM tblDevice d WHERE d.PlatformId = tblAppPlatform.PlatformId);");
        }
    }
}
