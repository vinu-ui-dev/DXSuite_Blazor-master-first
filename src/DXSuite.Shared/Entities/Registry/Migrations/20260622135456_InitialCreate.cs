using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Shared.Entities.Registry.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblApiKeyMapping",
                columns: table => new
                {
                    ApiKeyMappingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblApiKeyMapping", x => x.ApiKeyMappingId);
                });

            migrationBuilder.CreateTable(
                name: "tblTenant",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Subdomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ConnectionString_Director = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConnectionString_Sentinel = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConnectionString_CoreData = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConnectionString_Aviation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConnectionString_Design = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AuthConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModulesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SmtpConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FilePaths_MediaRoot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    MaxUsers = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblTenant", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblApiKeyMapping_ApiKeyHash",
                table: "tblApiKeyMapping",
                column: "ApiKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblApiKeyMapping_TenantId",
                table: "tblApiKeyMapping",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tblTenant_Subdomain",
                table: "tblTenant",
                column: "Subdomain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblApiKeyMapping");

            migrationBuilder.DropTable(
                name: "tblTenant");
        }
    }
}
