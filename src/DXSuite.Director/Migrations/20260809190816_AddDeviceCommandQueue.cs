using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceCommandQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblDeviceCommand",
                columns: table => new
                {
                    CommandId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    CommandType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    IssuedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDeviceCommand", x => x.CommandId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblDeviceCommand_Device_Status",
                table: "tblDeviceCommand",
                columns: new[] { "DeviceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDeviceCommand_RequestId",
                table: "tblDeviceCommand",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblDeviceCommand");
        }
    }
}
