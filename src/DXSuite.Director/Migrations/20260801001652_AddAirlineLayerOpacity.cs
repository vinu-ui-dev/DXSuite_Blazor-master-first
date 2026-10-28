using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddAirlineLayerOpacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 100 (fully opaque) so existing airline layers keep rendering unchanged.
            migrationBuilder.AddColumn<int>(
                name: "BackgroundOpacityPct",
                table: "tblUserGroupLocationDefault",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "MaskOpacityPct",
                table: "tblUserGroupLocationDefault",
                type: "int",
                nullable: false,
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundOpacityPct",
                table: "tblUserGroupLocationDefault");

            migrationBuilder.DropColumn(
                name: "MaskOpacityPct",
                table: "tblUserGroupLocationDefault");
        }
    }
}
