using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Design.Migrations
{
    /// <summary>
    /// Converts requirement categories from a fixed enum (stored as a string column) to per-design
    /// rows in tblDesignRequirementCategory, and adds SortOrder to both levels for user-controlled
    /// ordering. Every existing design is seeded with the six AVIXA default categories and its
    /// requirements are re-pointed at them, ordered by CreateDate, before the old column is dropped.
    /// </summary>
    public partial class RequirementCategoriesAndOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblDesignRequirementCategory",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DesignId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDesignRequirementCategory", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK_tblDesignRequirementCategory_tblDesign_DesignId",
                        column: x => x.DesignId,
                        principalTable: "tblDesign",
                        principalColumn: "DesignId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "tblDesignRequirement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "tblDesignRequirement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Seed the six AVIXA defaults for every existing design
            migrationBuilder.Sql("""
                INSERT INTO tblDesignRequirementCategory (DesignId, Name, SortOrder, CreateDate)
                SELECT d.DesignId, v.Name, v.SortOrder, GETUTCDATE()
                FROM tblDesign d
                CROSS JOIN (VALUES
                    ('Space & Environment', 10),
                    ('Functional Goals', 20),
                    ('User Experience', 30),
                    ('Technical Infrastructure', 40),
                    ('Budget & Schedule', 50),
                    ('Constraints', 60)
                ) v(Name, SortOrder);
                """);

            // Re-point each requirement at its design's category row matching the old enum string
            migrationBuilder.Sql("""
                UPDATE r SET r.CategoryId = c.CategoryId
                FROM tblDesignRequirement r
                JOIN (VALUES
                    ('SpaceAndEnvironment', 'Space & Environment'),
                    ('FunctionalGoals', 'Functional Goals'),
                    ('UserExperience', 'User Experience'),
                    ('TechnicalInfrastructure', 'Technical Infrastructure'),
                    ('BudgetAndSchedule', 'Budget & Schedule'),
                    ('Constraints', 'Constraints')
                ) v(EnumName, Name) ON r.Category = v.EnumName
                JOIN tblDesignRequirementCategory c ON c.DesignId = r.DesignId AND c.Name = v.Name;
                """);

            // Initial in-category ordering follows creation date, matching the previous display order
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT RequirementId,
                           ROW_NUMBER() OVER (PARTITION BY DesignId, CategoryId ORDER BY CreateDate, RequirementId) AS rn
                    FROM tblDesignRequirement
                )
                UPDATE r SET r.SortOrder = ranked.rn * 10
                FROM tblDesignRequirement r
                JOIN ranked ON r.RequirementId = ranked.RequirementId;
                """);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "tblDesignRequirement");

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignRequirement_CategoryId_SortOrder",
                table: "tblDesignRequirement",
                columns: new[] { "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignRequirementCategory_DesignId_Name",
                table: "tblDesignRequirementCategory",
                columns: new[] { "DesignId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tblDesignRequirementCategory_DesignId_SortOrder",
                table: "tblDesignRequirementCategory",
                columns: new[] { "DesignId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_tblDesignRequirement_tblDesignRequirementCategory_CategoryId",
                table: "tblDesignRequirement",
                column: "CategoryId",
                principalTable: "tblDesignRequirementCategory",
                principalColumn: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "tblDesignRequirement",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Restore the enum string from the category name; custom (non-default) categories
            // have no enum equivalent and fall back to FunctionalGoals.
            migrationBuilder.Sql("""
                UPDATE r SET r.Category = COALESCE(v.EnumName, 'FunctionalGoals')
                FROM tblDesignRequirement r
                JOIN tblDesignRequirementCategory c ON c.CategoryId = r.CategoryId
                LEFT JOIN (VALUES
                    ('SpaceAndEnvironment', 'Space & Environment'),
                    ('FunctionalGoals', 'Functional Goals'),
                    ('UserExperience', 'User Experience'),
                    ('TechnicalInfrastructure', 'Technical Infrastructure'),
                    ('BudgetAndSchedule', 'Budget & Schedule'),
                    ('Constraints', 'Constraints')
                ) v(EnumName, Name) ON c.Name = v.Name;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_tblDesignRequirement_tblDesignRequirementCategory_CategoryId",
                table: "tblDesignRequirement");

            migrationBuilder.DropTable(
                name: "tblDesignRequirementCategory");

            migrationBuilder.DropIndex(
                name: "IX_tblDesignRequirement_CategoryId_SortOrder",
                table: "tblDesignRequirement");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "tblDesignRequirement");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "tblDesignRequirement");
        }
    }
}
