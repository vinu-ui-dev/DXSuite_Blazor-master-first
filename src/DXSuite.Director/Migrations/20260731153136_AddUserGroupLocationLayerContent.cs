using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGroupLocationLayerContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblUserGroupLocationLayerContent",
                columns: table => new
                {
                    UserGroupLocationLayerContentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserGroupLocationDefaultId = table.Column<int>(type: "int", nullable: false),
                    LayerRole = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ContentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IntrinsicWidthPx = table.Column<int>(type: "int", nullable: false),
                    ContentMode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblUserGroupLocationLayerContent", x => x.UserGroupLocationLayerContentId);
                    table.ForeignKey(
                        name: "FK_tblUserGroupLocationLayerContent_tblUserGroupLocationDefault_UserGroupLocationDefaultId",
                        column: x => x.UserGroupLocationDefaultId,
                        principalTable: "tblUserGroupLocationDefault",
                        principalColumn: "UserGroupLocationDefaultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_tblUserGroupLocationLayerContent_DefaultRoleOrder",
                table: "tblUserGroupLocationLayerContent",
                columns: new[] { "UserGroupLocationDefaultId", "LayerRole", "SortOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblUserGroupLocationLayerContent");
        }
    }
}
