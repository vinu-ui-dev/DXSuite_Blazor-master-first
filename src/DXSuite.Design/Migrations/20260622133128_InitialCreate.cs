using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Design.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblDesign",
                columns: table => new
                {
                    DesignId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NetsuiteOpportunityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NetsuiteOpportunityData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesign", x => x.DesignId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignAttribute",
                columns: table => new
                {
                    AttributeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttributeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsGlobal = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignAttribute", x => x.AttributeId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCategoryGroup",
                columns: table => new
                {
                    CategoryGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCategoryGroup", x => x.CategoryGroupId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignLaborCategory",
                columns: table => new
                {
                    LaborCategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignLaborCategory", x => x.LaborCategoryId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignLaborColumn",
                columns: table => new
                {
                    ColumnId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignLaborColumn", x => x.ColumnId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignLaborType",
                columns: table => new
                {
                    LaborTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TypeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NetsuiteItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InternalCostPerHr = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InternalPricePerHr = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SubRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsPercentageOfColumns = table.Column<bool>(type: "bit", nullable: false),
                    ColumnPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    MinimumHours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsTravelingRole = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignLaborType", x => x.LaborTypeId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignMaterialsCategory",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NetsuiteItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignMaterialsCategory", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignMiscItem",
                columns: table => new
                {
                    MiscItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsTravel = table.Column<bool>(type: "bit", nullable: false),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DefaultSellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsPerPerson = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignMiscItem", x => x.MiscItemId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignNetsuiteConnection",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ConsumerKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConsumerSecret = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TokenId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TokenSecret = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CustomFormId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignNetsuiteConnection", x => x.ConnectionId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetConfig",
                columns: table => new
                {
                    ConfigId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetConfig", x => x.ConfigId);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignRequirement",
                columns: table => new
                {
                    RequirementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DesignId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ClientNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssignedToUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignRequirement", x => x.RequirementId);
                    table.ForeignKey(
                        name: "FK_tblDesignRequirement_tblDesign_DesignId",
                        column: x => x.DesignId,
                        principalTable: "tblDesign",
                        principalColumn: "DesignId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheet",
                columns: table => new
                {
                    WorksheetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DesignId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NetsuiteEstimateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NetsuiteEstimateTranId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DefaultLocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultLocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEditDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheet", x => x.WorksheetId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheet_tblDesign_DesignId",
                        column: x => x.DesignId,
                        principalTable: "tblDesign",
                        principalColumn: "DesignId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignAttributeOption",
                columns: table => new
                {
                    OptionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    OptionValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignAttributeOption", x => x.OptionId);
                    table.ForeignKey(
                        name: "FK_tblDesignAttributeOption_tblDesignAttribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "tblDesignAttribute",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCategory",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CategoryGroupId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCategory", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK_tblDesignCategory_tblDesignCategoryGroup_CategoryGroupId",
                        column: x => x.CategoryGroupId,
                        principalTable: "tblDesignCategoryGroup",
                        principalColumn: "CategoryGroupId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignLaborTypeColumn",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LaborTypeId = table.Column<int>(type: "int", nullable: false),
                    ColumnId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignLaborTypeColumn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tblDesignLaborTypeColumn_tblDesignLaborColumn_ColumnId",
                        column: x => x.ColumnId,
                        principalTable: "tblDesignLaborColumn",
                        principalColumn: "ColumnId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignLaborTypeColumn_tblDesignLaborType_LaborTypeId",
                        column: x => x.LaborTypeId,
                        principalTable: "tblDesignLaborType",
                        principalColumn: "LaborTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignMaterialsItem",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModelNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DefaultCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DefaultSellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignMaterialsItem", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_tblDesignMaterialsItem_tblDesignMaterialsCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "tblDesignMaterialsCategory",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignRequirementComment",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequirementId = table.Column<int>(type: "int", nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignRequirementComment", x => x.CommentId);
                    table.ForeignKey(
                        name: "FK_tblDesignRequirementComment_tblDesignRequirement_RequirementId",
                        column: x => x.RequirementId,
                        principalTable: "tblDesignRequirement",
                        principalColumn: "RequirementId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignTravelConfig",
                columns: table => new
                {
                    TravelConfigId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    Trips = table.Column<int>(type: "int", nullable: false),
                    TechnicianCount = table.Column<int>(type: "int", nullable: false),
                    AirfareCostPerPerson = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RentalCarCostPerDay = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RentalCarDays = table.Column<int>(type: "int", nullable: false),
                    HotelCostPerNight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HotelNights = table.Column<int>(type: "int", nullable: false),
                    PerDiemPerPerson = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PerDiemDays = table.Column<int>(type: "int", nullable: false),
                    FlatTripCharge = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignTravelConfig", x => x.TravelConfigId);
                    table.ForeignKey(
                        name: "FK_tblDesignTravelConfig_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetGroupDescription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    CategoryGroupId = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DescriptionItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DescriptionItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetGroupDescription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetGroupDescription_tblDesignCategoryGroup_CategoryGroupId",
                        column: x => x.CategoryGroupId,
                        principalTable: "tblDesignCategoryGroup",
                        principalColumn: "CategoryGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetGroupDescription_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetLaborCalculatorRow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    LaborTypeId = table.Column<int>(type: "int", nullable: false),
                    ColumnId = table.Column<int>(type: "int", nullable: false),
                    PersonCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetLaborCalculatorRow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLaborCalculatorRow_tblDesignLaborColumn_ColumnId",
                        column: x => x.ColumnId,
                        principalTable: "tblDesignLaborColumn",
                        principalColumn: "ColumnId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLaborCalculatorRow_tblDesignLaborType_LaborTypeId",
                        column: x => x.LaborTypeId,
                        principalTable: "tblDesignLaborType",
                        principalColumn: "LaborTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLaborCalculatorRow_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetLaborRow",
                columns: table => new
                {
                    LaborRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    LaborTypeId = table.Column<int>(type: "int", nullable: false),
                    CalculatedHours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    OverrideHours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    IsOverridden = table.Column<bool>(type: "bit", nullable: false),
                    SubCostPerHr = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SubPricePerHr = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PersonCount = table.Column<int>(type: "int", nullable: false),
                    PrintInPdf = table.Column<bool>(type: "bit", nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetLaborRow", x => x.LaborRowId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLaborRow_tblDesignLaborType_LaborTypeId",
                        column: x => x.LaborTypeId,
                        principalTable: "tblDesignLaborType",
                        principalColumn: "LaborTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLaborRow_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetMaterialsCategoryConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    PrintInPdf = table.Column<bool>(type: "bit", nullable: false),
                    IsDescEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DescriptionItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DescriptionItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetMaterialsCategoryConfig", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetMaterialsCategoryConfig_tblDesignMaterialsCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "tblDesignMaterialsCategory",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetMaterialsCategoryConfig_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetMiscRow",
                columns: table => new
                {
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    MiscItemId = table.Column<int>(type: "int", nullable: true),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    PrintInPdf = table.Column<bool>(type: "bit", nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetMiscRow", x => x.RowId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetMiscRow_tblDesignMiscItem_MiscItemId",
                        column: x => x.MiscItemId,
                        principalTable: "tblDesignMiscItem",
                        principalColumn: "MiscItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetMiscRow_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetNonInventoryRow",
                columns: table => new
                {
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    RowType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetNonInventoryRow", x => x.RowId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetNonInventoryRow_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetRoom",
                columns: table => new
                {
                    RoomId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    RoomName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetRoom", x => x.RoomId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetRoom_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetSectionDescription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DescriptionItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DescriptionItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetSectionDescription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetSectionDescription_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetTravelRow",
                columns: table => new
                {
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    MiscItemId = table.Column<int>(type: "int", nullable: true),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPerPerson = table.Column<bool>(type: "bit", nullable: false),
                    PrintInPdf = table.Column<bool>(type: "bit", nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetTravelRow", x => x.RowId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetTravelRow_tblDesignMiscItem_MiscItemId",
                        column: x => x.MiscItemId,
                        principalTable: "tblDesignMiscItem",
                        principalColumn: "MiscItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetTravelRow_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCatalogItem",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    NetsuiteItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSyncDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCatalogItem", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItem_tblDesignCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "tblDesignCategory",
                        principalColumn: "CategoryId");
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCategoryAttribute",
                columns: table => new
                {
                    AssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCategoryAttribute", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_tblDesignCategoryAttribute_tblDesignAttribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "tblDesignAttribute",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignCategoryAttribute_tblDesignCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "tblDesignCategory",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCategoryColumnAssignment",
                columns: table => new
                {
                    AssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    ColumnId = table.Column<int>(type: "int", nullable: false),
                    DefaultHours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCategoryColumnAssignment", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_tblDesignCategoryColumnAssignment_tblDesignCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "tblDesignCategory",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblDesignCategoryColumnAssignment_tblDesignLaborColumn_ColumnId",
                        column: x => x.ColumnId,
                        principalTable: "tblDesignLaborColumn",
                        principalColumn: "ColumnId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCategoryLaborAssignment",
                columns: table => new
                {
                    AssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    LaborTypeId = table.Column<int>(type: "int", nullable: false),
                    DefaultHours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCategoryLaborAssignment", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_tblDesignCategoryLaborAssignment_tblDesignCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "tblDesignCategory",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblDesignCategoryLaborAssignment_tblDesignLaborType_LaborTypeId",
                        column: x => x.LaborTypeId,
                        principalTable: "tblDesignLaborType",
                        principalColumn: "LaborTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetMaterialsRow",
                columns: table => new
                {
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetMaterialsRow", x => x.RowId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetMaterialsRow_tblDesignMaterialsItem_ItemId",
                        column: x => x.ItemId,
                        principalTable: "tblDesignMaterialsItem",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetMaterialsRow_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCatalogItemAttribute",
                columns: table => new
                {
                    ValueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCatalogItemAttribute", x => x.ValueId);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItemAttribute_tblDesignAttribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "tblDesignAttribute",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItemAttribute_tblDesignCatalogItem_ItemId",
                        column: x => x.ItemId,
                        principalTable: "tblDesignCatalogItem",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCatalogItemColumn",
                columns: table => new
                {
                    OverrideId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ColumnId = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCatalogItemColumn", x => x.OverrideId);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItemColumn_tblDesignCatalogItem_ItemId",
                        column: x => x.ItemId,
                        principalTable: "tblDesignCatalogItem",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItemColumn_tblDesignLaborColumn_ColumnId",
                        column: x => x.ColumnId,
                        principalTable: "tblDesignLaborColumn",
                        principalColumn: "ColumnId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignCatalogItemLabor",
                columns: table => new
                {
                    OverrideId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LaborTypeId = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignCatalogItemLabor", x => x.OverrideId);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItemLabor_tblDesignCatalogItem_ItemId",
                        column: x => x.ItemId,
                        principalTable: "tblDesignCatalogItem",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tblDesignCatalogItemLabor_tblDesignLaborType_LaborTypeId",
                        column: x => x.LaborTypeId,
                        principalTable: "tblDesignLaborType",
                        principalColumn: "LaborTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetLineItem",
                columns: table => new
                {
                    LineItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    RoomId = table.Column<int>(type: "int", nullable: true),
                    CatalogItemId = table.Column<int>(type: "int", nullable: true),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    DiscountPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MarginPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SystemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                    PrintInPdf = table.Column<bool>(type: "bit", nullable: false),
                    CategoryGroupIdOverride = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetLineItem", x => x.LineItemId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItem_tblDesignCatalogItem_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "tblDesignCatalogItem",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItem_tblDesignWorksheetRoom_RoomId",
                        column: x => x.RoomId,
                        principalTable: "tblDesignWorksheetRoom",
                        principalColumn: "RoomId");
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItem_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetLineItemAttr",
                columns: table => new
                {
                    LineItemAttrId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineItemId = table.Column<int>(type: "int", nullable: false),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetLineItemAttr", x => x.LineItemAttrId);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItemAttr_tblDesignAttribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "tblDesignAttribute",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItemAttr_tblDesignWorksheetLineItem_LineItemId",
                        column: x => x.LineItemId,
                        principalTable: "tblDesignWorksheetLineItem",
                        principalColumn: "LineItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tblDesignWorksheetLineItemColumnHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorksheetId = table.Column<int>(type: "int", nullable: false),
                    LineItemId = table.Column<int>(type: "int", nullable: false),
                    ColumnId = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignWorksheetLineItemColumnHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItemColumnHours_tblDesignLaborColumn_ColumnId",
                        column: x => x.ColumnId,
                        principalTable: "tblDesignLaborColumn",
                        principalColumn: "ColumnId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItemColumnHours_tblDesignWorksheetLineItem_LineItemId",
                        column: x => x.LineItemId,
                        principalTable: "tblDesignWorksheetLineItem",
                        principalColumn: "LineItemId");
                    table.ForeignKey(
                        name: "FK_tblDesignWorksheetLineItemColumnHours_tblDesignWorksheet_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "tblDesignWorksheet",
                        principalColumn: "WorksheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesign_CustomerId_NetsuiteOpportunityId",
                table: "tblDesign",
                columns: new[] { "CustomerId", "NetsuiteOpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignAttribute_CustomerId",
                table: "tblDesignAttribute",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignAttributeOption_AttributeId",
                table: "tblDesignAttributeOption",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItem_CategoryId",
                table: "tblDesignCatalogItem",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItem_CustomerId_NetsuiteItemId",
                table: "tblDesignCatalogItem",
                columns: new[] { "CustomerId", "NetsuiteItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItemAttribute_AttributeId",
                table: "tblDesignCatalogItemAttribute",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItemAttribute_ItemId_AttributeId",
                table: "tblDesignCatalogItemAttribute",
                columns: new[] { "ItemId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItemColumn_ColumnId",
                table: "tblDesignCatalogItemColumn",
                column: "ColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItemColumn_ItemId_ColumnId",
                table: "tblDesignCatalogItemColumn",
                columns: new[] { "ItemId", "ColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItemLabor_ItemId_LaborTypeId",
                table: "tblDesignCatalogItemLabor",
                columns: new[] { "ItemId", "LaborTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCatalogItemLabor_LaborTypeId",
                table: "tblDesignCatalogItemLabor",
                column: "LaborTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategory_CategoryGroupId",
                table: "tblDesignCategory",
                column: "CategoryGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryAttribute_AttributeId",
                table: "tblDesignCategoryAttribute",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryAttribute_CategoryId_AttributeId",
                table: "tblDesignCategoryAttribute",
                columns: new[] { "CategoryId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryColumnAssignment_CategoryId_ColumnId",
                table: "tblDesignCategoryColumnAssignment",
                columns: new[] { "CategoryId", "ColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryColumnAssignment_ColumnId",
                table: "tblDesignCategoryColumnAssignment",
                column: "ColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryGroup_CustomerId_Name",
                table: "tblDesignCategoryGroup",
                columns: new[] { "CustomerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryLaborAssignment_CategoryId_LaborTypeId",
                table: "tblDesignCategoryLaborAssignment",
                columns: new[] { "CategoryId", "LaborTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignCategoryLaborAssignment_LaborTypeId",
                table: "tblDesignCategoryLaborAssignment",
                column: "LaborTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignLaborCategory_CustomerId_Name",
                table: "tblDesignLaborCategory",
                columns: new[] { "CustomerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignLaborColumn_CustomerId_SortOrder",
                table: "tblDesignLaborColumn",
                columns: new[] { "CustomerId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignLaborType_CustomerId_IsActive",
                table: "tblDesignLaborType",
                columns: new[] { "CustomerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignLaborTypeColumn_ColumnId",
                table: "tblDesignLaborTypeColumn",
                column: "ColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignLaborTypeColumn_LaborTypeId_ColumnId",
                table: "tblDesignLaborTypeColumn",
                columns: new[] { "LaborTypeId", "ColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignMaterialsCategory_CustomerId_SortOrder",
                table: "tblDesignMaterialsCategory",
                columns: new[] { "CustomerId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignMaterialsItem_CategoryId",
                table: "tblDesignMaterialsItem",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignMaterialsItem_CustomerId_CategoryId_SortOrder",
                table: "tblDesignMaterialsItem",
                columns: new[] { "CustomerId", "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignMiscItem_CustomerId_IsTravel",
                table: "tblDesignMiscItem",
                columns: new[] { "CustomerId", "IsTravel" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignNetsuiteConnection_CustomerId_IsActive",
                table: "tblDesignNetsuiteConnection",
                columns: new[] { "CustomerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignRequirement_DesignId_Status",
                table: "tblDesignRequirement",
                columns: new[] { "DesignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignRequirementComment_RequirementId",
                table: "tblDesignRequirementComment",
                column: "RequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignTravelConfig_WorksheetId",
                table: "tblDesignTravelConfig",
                column: "WorksheetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheet_DesignId",
                table: "tblDesignWorksheet",
                column: "DesignId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetGroupDescription_CategoryGroupId",
                table: "tblDesignWorksheetGroupDescription",
                column: "CategoryGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetGroupDescription_WorksheetId_CategoryGroupId",
                table: "tblDesignWorksheetGroupDescription",
                columns: new[] { "WorksheetId", "CategoryGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLaborCalculatorRow_ColumnId",
                table: "tblDesignWorksheetLaborCalculatorRow",
                column: "ColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLaborCalculatorRow_LaborTypeId",
                table: "tblDesignWorksheetLaborCalculatorRow",
                column: "LaborTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLaborCalculatorRow_WorksheetId_LaborTypeId_ColumnId",
                table: "tblDesignWorksheetLaborCalculatorRow",
                columns: new[] { "WorksheetId", "LaborTypeId", "ColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLaborRow_LaborTypeId",
                table: "tblDesignWorksheetLaborRow",
                column: "LaborTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLaborRow_WorksheetId_LaborTypeId",
                table: "tblDesignWorksheetLaborRow",
                columns: new[] { "WorksheetId", "LaborTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItem_CatalogItemId",
                table: "tblDesignWorksheetLineItem",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItem_RoomId",
                table: "tblDesignWorksheetLineItem",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItem_WorksheetId",
                table: "tblDesignWorksheetLineItem",
                column: "WorksheetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItemAttr_AttributeId",
                table: "tblDesignWorksheetLineItemAttr",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItemAttr_LineItemId_AttributeId",
                table: "tblDesignWorksheetLineItemAttr",
                columns: new[] { "LineItemId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItemColumnHours_ColumnId",
                table: "tblDesignWorksheetLineItemColumnHours",
                column: "ColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItemColumnHours_LineItemId",
                table: "tblDesignWorksheetLineItemColumnHours",
                column: "LineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetLineItemColumnHours_WorksheetId_LineItemId_ColumnId",
                table: "tblDesignWorksheetLineItemColumnHours",
                columns: new[] { "WorksheetId", "LineItemId", "ColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetMaterialsCategoryConfig_CategoryId",
                table: "tblDesignWorksheetMaterialsCategoryConfig",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetMaterialsCategoryConfig_WorksheetId_CategoryId",
                table: "tblDesignWorksheetMaterialsCategoryConfig",
                columns: new[] { "WorksheetId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetMaterialsRow_ItemId",
                table: "tblDesignWorksheetMaterialsRow",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetMaterialsRow_WorksheetId_ItemId",
                table: "tblDesignWorksheetMaterialsRow",
                columns: new[] { "WorksheetId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetMiscRow_MiscItemId",
                table: "tblDesignWorksheetMiscRow",
                column: "MiscItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetMiscRow_WorksheetId",
                table: "tblDesignWorksheetMiscRow",
                column: "WorksheetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetNonInventoryRow_WorksheetId",
                table: "tblDesignWorksheetNonInventoryRow",
                column: "WorksheetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetRoom_WorksheetId",
                table: "tblDesignWorksheetRoom",
                column: "WorksheetId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetSectionDescription_WorksheetId_Section",
                table: "tblDesignWorksheetSectionDescription",
                columns: new[] { "WorksheetId", "Section" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetTravelRow_MiscItemId",
                table: "tblDesignWorksheetTravelRow",
                column: "MiscItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignWorksheetTravelRow_WorksheetId",
                table: "tblDesignWorksheetTravelRow",
                column: "WorksheetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblDesignAttributeOption");

            migrationBuilder.DropTable(
                name: "tblDesignCatalogItemAttribute");

            migrationBuilder.DropTable(
                name: "tblDesignCatalogItemColumn");

            migrationBuilder.DropTable(
                name: "tblDesignCatalogItemLabor");

            migrationBuilder.DropTable(
                name: "tblDesignCategoryAttribute");

            migrationBuilder.DropTable(
                name: "tblDesignCategoryColumnAssignment");

            migrationBuilder.DropTable(
                name: "tblDesignCategoryLaborAssignment");

            migrationBuilder.DropTable(
                name: "tblDesignLaborCategory");

            migrationBuilder.DropTable(
                name: "tblDesignLaborTypeColumn");

            migrationBuilder.DropTable(
                name: "tblDesignNetsuiteConnection");

            migrationBuilder.DropTable(
                name: "tblDesignRequirementComment");

            migrationBuilder.DropTable(
                name: "tblDesignTravelConfig");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetConfig");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetGroupDescription");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetLaborCalculatorRow");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetLaborRow");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetLineItemAttr");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetLineItemColumnHours");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetMaterialsCategoryConfig");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetMaterialsRow");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetMiscRow");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetNonInventoryRow");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetSectionDescription");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetTravelRow");

            migrationBuilder.DropTable(
                name: "tblDesignRequirement");

            migrationBuilder.DropTable(
                name: "tblDesignLaborType");

            migrationBuilder.DropTable(
                name: "tblDesignAttribute");

            migrationBuilder.DropTable(
                name: "tblDesignLaborColumn");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetLineItem");

            migrationBuilder.DropTable(
                name: "tblDesignMaterialsItem");

            migrationBuilder.DropTable(
                name: "tblDesignMiscItem");

            migrationBuilder.DropTable(
                name: "tblDesignCatalogItem");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheetRoom");

            migrationBuilder.DropTable(
                name: "tblDesignMaterialsCategory");

            migrationBuilder.DropTable(
                name: "tblDesignCategory");

            migrationBuilder.DropTable(
                name: "tblDesignWorksheet");

            migrationBuilder.DropTable(
                name: "tblDesignCategoryGroup");

            migrationBuilder.DropTable(
                name: "tblDesign");
        }
    }
}
