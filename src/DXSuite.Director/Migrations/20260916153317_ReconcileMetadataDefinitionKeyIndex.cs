using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Makes the model tell the truth about uniqueness on <c>tblMetadataDefinition.MetadataKey</c>.
    ///
    /// Deployed databases already carry a UNIQUE constraint, <c>UQ_MetadataDefinition_Key</c>, that no
    /// model declared and no migration created. The model now declares it, and this migration creates
    /// it only where the database has no unique index or constraint on that column under any name, so
    /// a database that has one is stamped without the DDL running and never gains a redundant second.
    /// </summary>
    public partial class ReconcileMetadataDefinitionKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('tblMetadataDefinition')
      AND i.is_unique = 1
      AND i.is_primary_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                  JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND c.name = 'MetadataKey'))
    ALTER TABLE tblMetadataDefinition ADD CONSTRAINT UQ_MetadataDefinition_Key UNIQUE (MetadataKey);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo safely. Most databases had this constraint before the migration existed,
            // and nothing records whether this one created it.
        }
    }
}
