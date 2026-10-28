using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGroupLocationLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblUserGroupLocationLogo",
                columns: table => new
                {
                    UserGroupLocationLogoId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserGroupLocationDefaultId = table.Column<int>(type: "int", nullable: false),
                    LogoRoleKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ContentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserGroupLocationLogo", x => x.UserGroupLocationLogoId);
                    table.ForeignKey(
                        name: "FK_tblUserGroupLocationLogo_tblUserGroupLocationDefault_UserGroupLocationDefaultId",
                        column: x => x.UserGroupLocationDefaultId,
                        principalTable: "tblUserGroupLocationDefault",
                        principalColumn: "UserGroupLocationDefaultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_tblUserGroupLocationLogo_DefaultRole",
                table: "tblUserGroupLocationLogo",
                columns: new[] { "UserGroupLocationDefaultId", "LogoRoleKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblUserGroupLocationLogo");
        }
    }
}
