using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddContentManifestLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblContentManifestLog",
                columns: table => new
                {
                    ContentManifestLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BucketType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    BucketId = table.Column<int>(type: "int", nullable: false),
                    FilterValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Version = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    PlayCount = table.Column<int>(type: "int", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblContentManifestLog", x => x.ContentManifestLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblContentManifestLog_Bucket",
                table: "tblContentManifestLog",
                columns: new[] { "BucketType", "BucketId", "FilterValue", "EffectiveFromUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblContentManifestLog");
        }
    }
}
