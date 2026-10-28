using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisioningBootstrapServedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BootstrapServedDate",
                table: "tblProvisioningAuthorization",
                type: "datetime",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootstrapServedDate",
                table: "tblProvisioningAuthorization");
        }
    }
}
