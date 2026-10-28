using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Sentinel.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayEnrollmentFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnrollmentFingerprint",
                table: "SentinelSiteGateway",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnrollmentFingerprint",
                table: "SentinelSiteGateway");
        }
    }
}
