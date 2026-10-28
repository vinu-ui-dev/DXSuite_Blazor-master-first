using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Design.Migrations
{
    /// <inheritdoc />
    public partial class DesignMcpTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblDesignMcpToken",
                columns: table => new
                {
                    TokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignMcpToken", x => x.TokenId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignMcpToken_CustomerId_UserId",
                table: "tblDesignMcpToken",
                columns: new[] { "CustomerId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignMcpToken_TokenHash",
                table: "tblDesignMcpToken",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblDesignMcpToken");
        }
    }
}
