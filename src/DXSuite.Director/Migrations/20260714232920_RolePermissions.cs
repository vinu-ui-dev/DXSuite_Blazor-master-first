using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class RolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseRoleId",
                table: "tblUserRole",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "tblUserRole",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tblKnownPermission",
                columns: table => new
                {
                    PermissionKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    DiscoveredDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblKnownPermission", x => x.PermissionKey);
                });

            migrationBuilder.CreateTable(
                name: "tblRolePermission",
                columns: table => new
                {
                    RolePermissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblRolePermission", x => x.RolePermissionId);
                    table.ForeignKey(
                        name: "FK_RolePermission_UserRole",
                        column: x => x.RoleId,
                        principalTable: "tblUserRole",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblUserRole_BaseRoleId",
                table: "tblUserRole",
                column: "BaseRoleId");

            migrationBuilder.CreateIndex(
                name: "UQ_RolePermission",
                table: "tblRolePermission",
                columns: new[] { "RoleId", "PermissionKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tblUserRole_BaseRole",
                table: "tblUserRole",
                column: "BaseRoleId",
                principalTable: "tblUserRole",
                principalColumn: "RoleId",
                onDelete: ReferentialAction.Restrict);

            // Existing databases already contain the four built-in roles; mark them
            // as system roles (fresh installs get IsSystem via director-seed.sql).
            // EXEC defers compilation to runtime: `dotnet ef migrations script` emits the whole
            // migration as one batch, and SQL Server binds column names at batch compile time,
            // so an inline UPDATE would fail with "Invalid column name 'IsSystem'" on databases
            // that don't yet have the column added above.
            migrationBuilder.Sql("EXEC('UPDATE tblUserRole SET IsSystem = 1 WHERE RoleId IN (1,2,3,4);');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tblUserRole_BaseRole",
                table: "tblUserRole");

            migrationBuilder.DropTable(
                name: "tblKnownPermission");

            migrationBuilder.DropTable(
                name: "tblRolePermission");

            migrationBuilder.DropIndex(
                name: "IX_tblUserRole_BaseRoleId",
                table: "tblUserRole");

            migrationBuilder.DropColumn(
                name: "BaseRoleId",
                table: "tblUserRole");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "tblUserRole");
        }
    }
}
