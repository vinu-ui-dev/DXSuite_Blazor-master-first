using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Records who owns an asset, and gives <c>view</c> access rules their own column.
    ///
    /// <c>tblLocationAccessRule.MetadataFilterKey</c> was overloaded: a metadata key on <c>partial</c>
    /// rules, retired because scope reads the location's participating keys instead, and a
    /// <c>__none__</c> / <c>__all__</c> scope on <c>view</c> rules, which moves into <c>ViewScope</c> as
    /// <c>none</c> / <c>all</c> before the column is dropped.
    /// </summary>
    public partial class AddAssetOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ViewScope",
                table: "tblLocationAccessRule",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            // EXEC keeps this valid when `migrations script` emits the migration as one batch (ViewScope is
            // created earlier in the same batch). A view rule holding anything but __all__ sees its scope.
            migrationBuilder.Sql(
                "EXEC('UPDATE tblLocationAccessRule SET ViewScope = CASE WHEN MetadataFilterKey = ''__all__'' THEN ''all'' ELSE ''none'' END WHERE AccessLevel = ''view''')");

            migrationBuilder.DropColumn(
                name: "MetadataFilterKey",
                table: "tblLocationAccessRule");

            migrationBuilder.AddColumn<int>(
                name: "UploadedByUserGroupId",
                table: "tblAsset",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadedByUserId",
                table: "tblAsset",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblAsset_UploadedByUserGroupId",
                table: "tblAsset",
                column: "UploadedByUserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblAsset_UploadedByUserId",
                table: "tblAsset",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Asset_UploadedByUser",
                table: "tblAsset",
                column: "UploadedByUserId",
                principalTable: "tblUser",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Asset_UploadedByUserGroup",
                table: "tblAsset",
                column: "UploadedByUserGroupId",
                principalTable: "tblUserGroup",
                principalColumn: "UserGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Asset_UploadedByUser",
                table: "tblAsset");

            migrationBuilder.DropForeignKey(
                name: "FK_Asset_UploadedByUserGroup",
                table: "tblAsset");

            migrationBuilder.DropIndex(
                name: "IX_tblAsset_UploadedByUserGroupId",
                table: "tblAsset");

            migrationBuilder.DropIndex(
                name: "IX_tblAsset_UploadedByUserId",
                table: "tblAsset");

            migrationBuilder.AddColumn<string>(
                name: "MetadataFilterKey",
                table: "tblLocationAccessRule",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            // A partial rule's metadata key is not recoverable; only view rules get their sentinel back.
            migrationBuilder.Sql(
                "EXEC('UPDATE tblLocationAccessRule SET MetadataFilterKey = CASE WHEN ViewScope = ''all'' THEN ''__all__'' ELSE ''__none__'' END WHERE AccessLevel = ''view''')");

            migrationBuilder.DropColumn(
                name: "ViewScope",
                table: "tblLocationAccessRule");

            migrationBuilder.DropColumn(
                name: "UploadedByUserGroupId",
                table: "tblAsset");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "tblAsset");
        }
    }
}
