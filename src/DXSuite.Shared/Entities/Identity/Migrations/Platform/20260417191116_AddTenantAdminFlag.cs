using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Shared.Entities.Identity.Migrations.Platform
{
    /// <inheritdoc />
    public partial class AddTenantAdminFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTenantAdmin",
                table: "tblPlatformUserTenantMembership",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTenantAdmin",
                table: "tblPlatformUserTenantMembership");
        }
    }
}
