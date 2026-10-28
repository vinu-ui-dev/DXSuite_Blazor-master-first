using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Lets an app bundle and a bootloader share a version number under one platform row, which the
    /// code has always assumed and the database has always forbidden.
    ///
    /// <c>UQ_tblAppVersion_Platform_Version</c> is a UNIQUE KEY constraint on (PlatformId, VersionNumber) and
    /// predates artifact kinds by years. <c>AddBrightSignArtifactKinds</c> added the column and left
    /// the index alone, so <c>CreateVersionAsync</c> checks for duplicates on
    /// (PlatformId, ArtifactKind, VersionNumber), passes, and then fails on the insert with nothing
    /// but "An error occurred while saving the entity changes."
    ///
    /// It is not a theoretical conflict. Both Engage for BrightSign artifacts start at 2.0.0
    /// deliberately, because the version line starts there and the two halves ship together the
    /// first time; uploading the bootloader and then the bundle is the first real use of the
    /// pipeline and it was the thing that could not be done.
    ///
    /// The index is raw SQL here because it exists only in the database: nothing in the EF model
    /// declares it, so no scaffolded migration would ever regenerate it and the model snapshot does
    /// not change.
    /// </summary>
    public partial class WidenAppVersionUniqueIndexToArtifactKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ALTER TABLE rather than DROP INDEX: this is a UNIQUE KEY constraint, and SQL Server
            // refuses an explicit DROP INDEX on the index enforcing one.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblAppVersion_Platform_Version' AND parent_object_id = OBJECT_ID('tblAppVersion'))
    ALTER TABLE tblAppVersion DROP CONSTRAINT UQ_tblAppVersion_Platform_Version;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblAppVersion_Platform_Kind_Version' AND parent_object_id = OBJECT_ID('tblAppVersion'))
    ALTER TABLE tblAppVersion ADD CONSTRAINT UQ_tblAppVersion_Platform_Kind_Version
        UNIQUE (PlatformId, ArtifactKind, VersionNumber);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring the narrower index fails if two kinds already share a version number, which
            // is correct: going back means those rows cannot coexist, and a down-migration must not
            // decide which of them to destroy.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblAppVersion_Platform_Kind_Version' AND parent_object_id = OBJECT_ID('tblAppVersion'))
    ALTER TABLE tblAppVersion DROP CONSTRAINT UQ_tblAppVersion_Platform_Kind_Version;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblAppVersion_Platform_Version' AND parent_object_id = OBJECT_ID('tblAppVersion'))
    ALTER TABLE tblAppVersion ADD CONSTRAINT UQ_tblAppVersion_Platform_Version
        UNIQUE (PlatformId, VersionNumber);");
        }
    }
}
