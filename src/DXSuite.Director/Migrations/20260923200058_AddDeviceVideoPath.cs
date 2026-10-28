using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceVideoPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VideoPath",
                table: "tblDevice",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "Hardware");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoPath",
                table: "tblDevice");
        }
    }
}
