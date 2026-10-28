using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Adds the opt-in that lets a deployment send a device to an older version than it runs.
    /// Defaults to 0, so every deployment that already exists becomes forward-only.
    /// </summary>
    public partial class AddDeploymentAllowDowngrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowDowngrade",
                table: "tblUpdateDeployment",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowDowngrade", table: "tblUpdateDeployment");
        }
    }
}
