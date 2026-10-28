using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMoxiePlaylistLocationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PlaylistLocation_Omnivex.razor is deleted, so this seeded type resolves to no
            // component. Location.razor dispatches on ComponentTypeName by reflection and
            // renders nothing when the type is missing, which is why the row goes too.
            // Rows still referenced by a location are left alone rather than orphaning them;
            // such a location renders blank until it is moved to the Playlist type.
            migrationBuilder.Sql(@"
DELETE FROM tblLocationType
WHERE ComponentTypeName = 'DXSuite.Director.Components.LocationTypes.PlaylistLocation_Omnivex'
  AND NOT EXISTS (SELECT 1 FROM tblLocation WHERE tblLocation.LocationTypeId = tblLocationType.LocationTypeId);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Restoring the row would point a location type at a component
            // that no longer exists in the assembly, which is worse than leaving it absent.
        }
    }
}
