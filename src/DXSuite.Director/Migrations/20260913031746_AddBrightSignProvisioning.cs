using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddBrightSignProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryTokenCollectedAt",
                table: "tblDevicePairingRequest",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryTokenCreatedAt",
                table: "tblDevicePairingRequest",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTokenProtected",
                table: "tblDevicePairingRequest",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DwsPasswordProtected",
                table: "tblDevice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DwsPasswordRotatedAt",
                table: "tblDevice",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningFingerprint",
                table: "tblDevice",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tblFirmwareImage",
                columns: table => new
                {
                    FirmwareImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Family = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    VersionNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    IsDefaultForFamily = table.Column<bool>(type: "bit", nullable: false),
                    UploadedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    UploadedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblFirmwareImage", x => x.FirmwareImageId);
                });

            migrationBuilder.CreateTable(
                name: "tblProvisioningPackage",
                columns: table => new
                {
                    ProvisioningPackageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    HardwareFamily = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    FirmwareImageId = table.Column<int>(type: "int", nullable: false),
                    FirmwarePinOverridden = table.Column<bool>(type: "bit", nullable: false),
                    EndpointUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    TimeSourceUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    CustomRootCaPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProxyHostPort = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    WifiSsid = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    WifiPskProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DwsPosition = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    BootstrapDwsPasswordProtected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WebInspectorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ConfigFingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ZipFileName = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    ZipFilePath = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    ZipFileSize = table.Column<long>(type: "bigint", nullable: true),
                    ZipSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    RevokedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    ModifiedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblProvisioningPackage", x => x.ProvisioningPackageId);
                    table.ForeignKey(
                        name: "FK_tblProvisioningPackage_tblFirmwareImage_FirmwareImageId",
                        column: x => x.FirmwareImageId,
                        principalTable: "tblFirmwareImage",
                        principalColumn: "FirmwareImageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblProvisioningAuthorization",
                columns: table => new
                {
                    ProvisioningAuthorizationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SerialNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ArmedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ArmedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    ExpiresDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    PinnedAppVersionId = table.Column<int>(type: "int", nullable: true),
                    PinnedBootloaderVersionId = table.Column<int>(type: "int", nullable: true),
                    ProvisioningPackageId = table.Column<int>(type: "int", nullable: true),
                    ConvertedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ConvertedDeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    DisarmedBy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    DisarmedDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblProvisioningAuthorization", x => x.ProvisioningAuthorizationId);
                    table.ForeignKey(
                        name: "FK_tblProvisioningAuthorization_tblAppVersion_PinnedAppVersionId",
                        column: x => x.PinnedAppVersionId,
                        principalTable: "tblAppVersion",
                        principalColumn: "AppVersionId");
                    table.ForeignKey(
                        name: "FK_tblProvisioningAuthorization_tblAppVersion_PinnedBootloaderVersionId",
                        column: x => x.PinnedBootloaderVersionId,
                        principalTable: "tblAppVersion",
                        principalColumn: "AppVersionId");
                    table.ForeignKey(
                        name: "FK_tblProvisioningAuthorization_tblProvisioningPackage_ProvisioningPackageId",
                        column: x => x.ProvisioningPackageId,
                        principalTable: "tblProvisioningPackage",
                        principalColumn: "ProvisioningPackageId");
                });

            migrationBuilder.CreateIndex(
                name: "UX_tblFirmwareImage_Family_Version",
                table: "tblFirmwareImage",
                columns: new[] { "Family", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningAuthorization_PinnedAppVersionId",
                table: "tblProvisioningAuthorization",
                column: "PinnedAppVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningAuthorization_PinnedBootloaderVersionId",
                table: "tblProvisioningAuthorization",
                column: "PinnedBootloaderVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningAuthorization_ProvisioningPackageId",
                table: "tblProvisioningAuthorization",
                column: "ProvisioningPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningAuthorization_Serial",
                table: "tblProvisioningAuthorization",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningAuthorization_State",
                table: "tblProvisioningAuthorization",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningPackage_Fingerprint",
                table: "tblProvisioningPackage",
                column: "ConfigFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_tblProvisioningPackage_FirmwareImageId",
                table: "tblProvisioningPackage",
                column: "FirmwareImageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblProvisioningAuthorization");

            migrationBuilder.DropTable(
                name: "tblProvisioningPackage");

            migrationBuilder.DropTable(
                name: "tblFirmwareImage");

            migrationBuilder.DropColumn(
                name: "DeliveryTokenCollectedAt",
                table: "tblDevicePairingRequest");

            migrationBuilder.DropColumn(
                name: "DeliveryTokenCreatedAt",
                table: "tblDevicePairingRequest");

            migrationBuilder.DropColumn(
                name: "DeliveryTokenProtected",
                table: "tblDevicePairingRequest");

            migrationBuilder.DropColumn(
                name: "DwsPasswordProtected",
                table: "tblDevice");

            migrationBuilder.DropColumn(
                name: "DwsPasswordRotatedAt",
                table: "tblDevice");

            migrationBuilder.DropColumn(
                name: "ProvisioningFingerprint",
                table: "tblDevice");
        }
    }
}
