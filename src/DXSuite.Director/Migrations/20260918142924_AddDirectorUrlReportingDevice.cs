using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorUrlReportingDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeviceInstanceId",
                table: "tblDirectorUrl",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDirectorUrl_DeviceInstanceId",
                table: "tblDirectorUrl",
                column: "DeviceInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tblDirectorUrl_DeviceInstanceId",
                table: "tblDirectorUrl");

            migrationBuilder.DropColumn(
                name: "DeviceInstanceId",
                table: "tblDirectorUrl");
        }
    }
}
