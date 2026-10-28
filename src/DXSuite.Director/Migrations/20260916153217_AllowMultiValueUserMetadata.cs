using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Lets a user or group hold several values for one metadata key. A user's scope is their own
    /// values unioned with their groups', and a scoped user can legitimately cover two concourses.
    ///
    /// The composite unique indexes are found by their columns, not their names: the model called
    /// them <c>UQ_UserMetadata</c> / <c>UQ_UserGroupMetadata</c>, while deployed databases carry
    /// <c>IX_UserMetadata_UserKey</c> / <c>IX_UserGroupMetadata_GroupKey</c>; see
    /// <see cref="MetadataIndexSql"/>. The single-column indexes on UserId and UserGroupId stay, so
    /// lookups by subject are still indexed.
    ///
    /// This must precede any code that writes a second value for a user or group key.
    /// </summary>
    public partial class AllowMultiValueUserMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MetadataIndexSql.DropUniqueOnColumns("tblUserMetadata", "UserId", "MetadataKey"));
            migrationBuilder.Sql(MetadataIndexSql.DropUniqueOnColumns("tblUserGroupMetadata", "UserGroupId", "MetadataKey"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails if any subject now holds two values for one key, which is correct: going back means
            // those rows cannot coexist, and a down-migration must not decide which of them to destroy.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblUserMetadata') AND name = 'UQ_UserMetadata')
    CREATE UNIQUE INDEX UQ_UserMetadata ON tblUserMetadata (UserId, MetadataKey);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblUserGroupMetadata') AND name = 'UQ_UserGroupMetadata')
    CREATE UNIQUE INDEX UQ_UserGroupMetadata ON tblUserGroupMetadata (UserGroupId, MetadataKey);");
        }
    }
}
