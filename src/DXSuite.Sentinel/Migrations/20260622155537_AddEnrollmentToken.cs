using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Sentinel.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentinelEnrollmentToken",
                columns: table => new
                {
                    TokenID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    DefaultRoles = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    UseCount = table.Column<int>(type: "int", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelEnrollmentToken", x => x.TokenID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentinelEnrollmentToken_CustomerID",
                table: "SentinelEnrollmentToken",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelEnrollmentToken_TokenHash",
                table: "SentinelEnrollmentToken",
                column: "TokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentinelEnrollmentToken");
        }
    }
}
