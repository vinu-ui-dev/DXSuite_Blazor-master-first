using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblMessageType",
                columns: table => new
                {
                    MessageTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    DisplayMode = table.Column<byte>(type: "tinyint", nullable: false),
                    AllowsSignVideo = table.Column<bool>(type: "bit", nullable: false),
                    DefaultDisplaySeconds = table.Column<int>(type: "int", nullable: false),
                    DefaultHideSeconds = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMessageType", x => x.MessageTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblZone",
                columns: table => new
                {
                    ZoneId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZoneName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblZone", x => x.ZoneId);
                });

            migrationBuilder.CreateTable(
                name: "tblMessage",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageTypeId = table.Column<int>(type: "int", nullable: false),
                    OwningUserGroupId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsCanned = table.Column<bool>(type: "bit", nullable: false),
                    SignVideoAssetId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMessage", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_tblMessage_OwningUserGroup",
                        column: x => x.OwningUserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblMessage_SignVideoAsset",
                        column: x => x.SignVideoAssetId,
                        principalTable: "tblAsset",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblMessage_tblMessageType",
                        column: x => x.MessageTypeId,
                        principalTable: "tblMessageType",
                        principalColumn: "MessageTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblMessagePermission",
                columns: table => new
                {
                    MessagePermissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserGroupId = table.Column<int>(type: "int", nullable: false),
                    MessageTypeId = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: true),
                    CanFreeText = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMessagePermission", x => x.MessagePermissionId);
                    table.ForeignKey(
                        name: "FK_tblMessagePermission_UserGroup",
                        column: x => x.UserGroupId,
                        principalTable: "tblUserGroup",
                        principalColumn: "UserGroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblMessagePermission_tblMessageType",
                        column: x => x.MessageTypeId,
                        principalTable: "tblMessageType",
                        principalColumn: "MessageTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblMessagePermission_tblZone",
                        column: x => x.ZoneId,
                        principalTable: "tblZone",
                        principalColumn: "ZoneId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblZoneDisplay",
                columns: table => new
                {
                    ZoneId = table.Column<int>(type: "int", nullable: false),
                    DisplayId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblZoneDisplay", x => new { x.ZoneId, x.DisplayId });
                    table.ForeignKey(
                        name: "FK_tblZoneDisplay_tblDisplay",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblZoneDisplay_tblZone",
                        column: x => x.ZoneId,
                        principalTable: "tblZone",
                        principalColumn: "ZoneId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblMessageActivation",
                columns: table => new
                {
                    MessageActivationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PriorityOverride = table.Column<int>(type: "int", nullable: true),
                    DisplaySecondsOverride = table.Column<int>(type: "int", nullable: true),
                    HideSecondsOverride = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    ActivatedByUserId = table.Column<int>(type: "int", nullable: true),
                    ActivatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContentSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMessageActivation", x => x.MessageActivationId);
                    table.ForeignKey(
                        name: "FK_tblMessageActivation_tblMessage",
                        column: x => x.MessageId,
                        principalTable: "tblMessage",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblMessageText",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    LanguageCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMessageText", x => new { x.MessageId, x.LanguageCode });
                    table.ForeignKey(
                        name: "FK_tblMessageText_tblMessage",
                        column: x => x.MessageId,
                        principalTable: "tblMessage",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblMessageActivationTarget",
                columns: table => new
                {
                    MessageActivationTargetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageActivationId = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: true),
                    DisplayId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblMessageActivationTarget", x => x.MessageActivationTargetId);
                    table.CheckConstraint("CK_tblMessageActivationTarget_OneTarget", "(CASE WHEN [ZoneId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DisplayId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LocationId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_tblMessageActivationTarget_tblDisplay",
                        column: x => x.DisplayId,
                        principalTable: "tblDisplay",
                        principalColumn: "DisplayId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblMessageActivationTarget_tblLocation",
                        column: x => x.LocationId,
                        principalTable: "tblLocation",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblMessageActivationTarget_tblMessageActivation",
                        column: x => x.MessageActivationId,
                        principalTable: "tblMessageActivation",
                        principalColumn: "MessageActivationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblMessageActivationTarget_tblZone",
                        column: x => x.ZoneId,
                        principalTable: "tblZone",
                        principalColumn: "ZoneId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "tblMessageType",
                columns: new[] { "MessageTypeId", "AllowsSignVideo", "Code", "DefaultDisplaySeconds", "DefaultHideSeconds", "DisplayMode", "DisplayName", "Priority" },
                values: new object[,]
                {
                    { 1, true, "EMERGENCY", 10, 30, (byte)1, "Emergency", 10 },
                    { 2, false, "IRROPS", 10, 30, (byte)2, "IRROPS", 20 },
                    { 3, true, "VISUAL_PAGING", 10, 30, (byte)2, "Visual Paging", 40 },
                    { 4, true, "COUNTER", 10, 30, (byte)3, "Counter", 50 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblMessage_MessageTypeId",
                table: "tblMessage",
                column: "MessageTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessage_OwningUserGroupId",
                table: "tblMessage",
                column: "OwningUserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessage_SignVideoAssetId",
                table: "tblMessage",
                column: "SignVideoAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessageActivation_MessageId",
                table: "tblMessageActivation",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessageActivation_Window",
                table: "tblMessageActivation",
                columns: new[] { "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_tblMessageActivationTarget_DisplayId",
                table: "tblMessageActivationTarget",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessageActivationTarget_LocationId",
                table: "tblMessageActivationTarget",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessageActivationTarget_MessageActivationId",
                table: "tblMessageActivationTarget",
                column: "MessageActivationId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessageActivationTarget_ZoneId",
                table: "tblMessageActivationTarget",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessagePermission_MessageTypeId",
                table: "tblMessagePermission",
                column: "MessageTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblMessagePermission_ZoneId",
                table: "tblMessagePermission",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "UX_tblMessagePermission_Group_Type_Zone",
                table: "tblMessagePermission",
                columns: new[] { "UserGroupId", "MessageTypeId", "ZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_tblMessageType_Code",
                table: "tblMessageType",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_tblZone_ZoneName",
                table: "tblZone",
                column: "ZoneName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblZoneDisplay_DisplayId",
                table: "tblZoneDisplay",
                column: "DisplayId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblMessageActivationTarget");

            migrationBuilder.DropTable(
                name: "tblMessagePermission");

            migrationBuilder.DropTable(
                name: "tblMessageText");

            migrationBuilder.DropTable(
                name: "tblZoneDisplay");

            migrationBuilder.DropTable(
                name: "tblMessageActivation");

            migrationBuilder.DropTable(
                name: "tblZone");

            migrationBuilder.DropTable(
                name: "tblMessage");

            migrationBuilder.DropTable(
                name: "tblMessageType");
        }
    }
}
