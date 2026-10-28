using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Sentinel.Migrations
{
    /// <inheritdoc />
    public partial class AddPulseIssueLevelDoNotReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DoNotReport",
                table: "SentinelPulseIssueLevel",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoNotReport",
                table: "SentinelPulseIssueLevel");
        }
    }
}
