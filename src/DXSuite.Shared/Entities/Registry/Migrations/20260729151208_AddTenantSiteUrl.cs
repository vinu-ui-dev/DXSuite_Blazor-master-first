using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Shared.Entities.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSiteUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteUrl",
                table: "tblTenant",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteUrl",
                table: "tblTenant");
        }
    }
}
