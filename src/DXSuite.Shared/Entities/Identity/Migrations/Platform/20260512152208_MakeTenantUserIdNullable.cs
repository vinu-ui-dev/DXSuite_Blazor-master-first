using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Shared.Entities.Identity.Migrations.Platform
{
    /// <inheritdoc />
    public partial class MakeTenantUserIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TenantUserId",
                table: "tblPlatformUserTenantMembership",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TenantUserId",
                table: "tblPlatformUserTenantMembership",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
