using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "tblDevice",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tblSite",
                columns: table => new
                {
                    SiteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address1 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StateProvince = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblSite", x => x.SiteId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblDevice_SiteId",
                table: "tblDevice",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_tblDevice_tblSite",
                table: "tblDevice",
                column: "SiteId",
                principalTable: "tblSite",
                principalColumn: "SiteId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tblDevice_tblSite",
                table: "tblDevice");

            migrationBuilder.DropTable(
                name: "tblSite");

            migrationBuilder.DropIndex(
                name: "IX_tblDevice_SiteId",
                table: "tblDevice");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "tblDevice");
        }
    }
}
