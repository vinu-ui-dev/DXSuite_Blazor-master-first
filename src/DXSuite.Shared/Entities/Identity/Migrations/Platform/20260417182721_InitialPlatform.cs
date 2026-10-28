using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DXSuite.Shared.Entities.Identity.Migrations.Platform
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblPlatformAuditLog",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    EventTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResourcePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TicketRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPlatformAuditLog", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "tblPlatformRole",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPlatformRole", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "tblPlatformUser",
                columns: table => new
                {
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    PrimaryEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPlatformOperator = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPlatformUser", x => x.PlatformUserId);
                });

            migrationBuilder.CreateTable(
                name: "tblTenantAccessNotice",
                columns: table => new
                {
                    NoticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    OperatorDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ElevationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TicketRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResourcePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblTenantAccessNotice", x => x.NoticeId);
                });

            migrationBuilder.CreateTable(
                name: "tblTenantSettings",
                columns: table => new
                {
                    TenantSettingsId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequireTenantApprovalForElevation = table.Column<bool>(type: "bit", nullable: false),
                    ElevationMaxDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    NotifyOnElevationEmailsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BlockedRegionsJson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblTenantSettings", x => x.TenantSettingsId);
                });

            migrationBuilder.CreateTable(
                name: "tblPlatformElevation",
                columns: table => new
                {
                    ElevationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TicketRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPlatformElevation", x => x.ElevationId);
                    table.ForeignKey(
                        name: "FK_PlatformElevation_User",
                        column: x => x.PlatformUserId,
                        principalTable: "tblPlatformUser",
                        principalColumn: "PlatformUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblPlatformUserRole",
                columns: table => new
                {
                    PlatformUserRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    GrantedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GrantedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPlatformUserRole", x => x.PlatformUserRoleId);
                    table.ForeignKey(
                        name: "FK_PlatformUserRole_Role",
                        column: x => x.RoleId,
                        principalTable: "tblPlatformRole",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformUserRole_User",
                        column: x => x.PlatformUserId,
                        principalTable: "tblPlatformUser",
                        principalColumn: "PlatformUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblPlatformUserTenantMembership",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantUserId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    GrantedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    GrantedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblPlatformUserTenantMembership", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_PlatformMembership_PlatformUser",
                        column: x => x.PlatformUserId,
                        principalTable: "tblPlatformUser",
                        principalColumn: "PlatformUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "tblPlatformRole",
                columns: new[] { "RoleId", "Description", "RoleName" },
                values: new object[,]
                {
                    { 1, "Manage tenants, operators, and billing. Cannot read tenant business data.", "PlatformOperator" },
                    { 2, "Time-bound read access into a specific tenant for support purposes.", "TenantSupport" },
                    { 3, "Time-bound read+write access into a specific tenant. Requires second approver.", "TenantSupportWrite" },
                    { 4, "Read-only access to platform audit logs across all tenants. No tenant data access.", "Auditor" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformElevation_UserTenantExpiry",
                table: "tblPlatformElevation",
                columns: new[] { "PlatformUserId", "TenantId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformRole_RoleName",
                table: "tblPlatformRole",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformUser_PrimaryEmail",
                table: "tblPlatformUser",
                column: "PrimaryEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblPlatformUserRole_RoleId",
                table: "tblPlatformUserRole",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformUserRole",
                table: "tblPlatformUserRole",
                columns: new[] { "PlatformUserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformMembership_UserTenant",
                table: "tblPlatformUserTenantMembership",
                columns: new[] { "PlatformUserId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAccessNotice_TenantTime",
                table: "tblTenantAccessNotice",
                columns: new[] { "TenantId", "EventTime" });

            migrationBuilder.CreateIndex(
                name: "UQ_TenantSettings_TenantId",
                table: "tblTenantSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblPlatformAuditLog");

            migrationBuilder.DropTable(
                name: "tblPlatformElevation");

            migrationBuilder.DropTable(
                name: "tblPlatformUserRole");

            migrationBuilder.DropTable(
                name: "tblPlatformUserTenantMembership");

            migrationBuilder.DropTable(
                name: "tblTenantAccessNotice");

            migrationBuilder.DropTable(
                name: "tblTenantSettings");

            migrationBuilder.DropTable(
                name: "tblPlatformRole");

            migrationBuilder.DropTable(
                name: "tblPlatformUser");
        }
    }
}
