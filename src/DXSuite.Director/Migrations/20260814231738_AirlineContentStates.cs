using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AirlineContentStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_tblUserGroupLocationDefault_GroupLocation",
                table: "tblUserGroupLocationDefault");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultState",
                table: "tblUserGroupLocationDefault",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StateName",
                table: "tblUserGroupLocationDefault",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StateScheduleJson",
                table: "tblUserGroupLocationDefault",
                type: "nvarchar(max)",
                nullable: true);

            // Existing rows become the sole default state of their (group, location) pair.
            // EXEC keeps this valid when `migrations script` emits the migration as one batch
            // (the columns referenced here are created earlier in the same batch).
            migrationBuilder.Sql(
                "EXEC('UPDATE tblUserGroupLocationDefault SET StateName = ''Default'', IsDefaultState = 1')");

            migrationBuilder.CreateIndex(
                name: "UX_tblUserGroupLocationDefault_GroupLocationState",
                table: "tblUserGroupLocationDefault",
                columns: new[] { "UserGroupId", "LocationId", "StateName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_tblUserGroupLocationDefault_GroupLocationState",
                table: "tblUserGroupLocationDefault");

            migrationBuilder.DropColumn(
                name: "IsDefaultState",
                table: "tblUserGroupLocationDefault");

            migrationBuilder.DropColumn(
                name: "StateName",
                table: "tblUserGroupLocationDefault");

            migrationBuilder.DropColumn(
                name: "StateScheduleJson",
                table: "tblUserGroupLocationDefault");

            migrationBuilder.CreateIndex(
                name: "UX_tblUserGroupLocationDefault_GroupLocation",
                table: "tblUserGroupLocationDefault",
                columns: new[] { "UserGroupId", "LocationId" },
                unique: true);
        }
    }
}
