using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Lets an asset hold several values for one metadata key, one row each, and splits the
    /// comma-joined values that the multi-select editor wrote before rows could hold them.
    ///
    /// The order is load-bearing: the old unique index on (AssetId, MetadataKey) is dropped first,
    /// because the split inserts a second row for the same pair; the new indexes are created last.
    ///
    /// A value is split only when its key is a <c>multi-select</c> registry key and every
    /// comma-separated piece is one of that key's declared options. Anything else is left whole, a
    /// single value that happens to contain a comma, and listed in the migration output by asset and key.
    ///
    /// Index DDL is raw SQL found through the catalog; see <see cref="MetadataIndexSql"/> for why.
    /// </summary>
    public partial class AllowMultiValueAssetMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MetadataIndexSql.DropUniqueOnColumns("tblAssetContentMetadata", "AssetId", "MetadataKey"));

            migrationBuilder.Sql(@"
SELECT m.AssetContentMetadataId, m.AssetId, m.MetadataKey, m.MetadataValue, m.SetByUserId, m.CreateDate
INTO #splitMetadata
FROM tblAssetContentMetadata m
JOIN tblMetadataDefinition d ON d.MetadataKey = m.MetadataKey
WHERE d.FieldType = 'multi-select'
  AND ISJSON(d.OptionsJson) = 1
  AND m.MetadataValue LIKE '%,%'
  AND NOT EXISTS (
      SELECT 1 FROM STRING_SPLIT(m.MetadataValue, ',') piece
      WHERE NOT EXISTS (
          SELECT 1 FROM OPENJSON(d.OptionsJson) opt
          WHERE LTRIM(RTRIM(opt.value)) COLLATE DATABASE_DEFAULT = LTRIM(RTRIM(piece.value)) COLLATE DATABASE_DEFAULT));

INSERT INTO tblAssetContentMetadata (AssetId, MetadataKey, MetadataValue, SetByUserId, CreateDate)
SELECT DISTINCT s.AssetId, s.MetadataKey, LTRIM(RTRIM(piece.value)), s.SetByUserId, s.CreateDate
FROM #splitMetadata s
CROSS APPLY STRING_SPLIT(s.MetadataValue, ',') piece
WHERE NOT EXISTS (
    SELECT 1 FROM tblAssetContentMetadata existing
    WHERE existing.AssetId = s.AssetId
      AND existing.MetadataKey = s.MetadataKey
      AND existing.MetadataValue = LTRIM(RTRIM(piece.value)));

DELETE m
FROM tblAssetContentMetadata m
JOIN #splitMetadata s ON s.AssetContentMetadataId = m.AssetContentMetadataId;

DECLARE @unsplitMetadata nvarchar(max);
SELECT @unsplitMetadata = STRING_AGG(CAST(CONCAT('AssetId ', AssetId, ' key ', MetadataKey) AS nvarchar(max)), '; ')
FROM tblAssetContentMetadata
WHERE MetadataValue LIKE '%,%';
IF @unsplitMetadata IS NOT NULL
    PRINT 'Asset metadata values containing a comma were left whole: ' + @unsplitMetadata;

DROP TABLE #splitMetadata;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblAssetContentMetadata') AND name = 'IX_ContentMetadata_AssetKey')
    CREATE INDEX IX_ContentMetadata_AssetKey ON tblAssetContentMetadata (AssetId, MetadataKey) INCLUDE (MetadataValue);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblAssetContentMetadata') AND name = 'UQ_ContentMetadata_AssetKeyValue')
    CREATE UNIQUE INDEX UQ_ContentMetadata_AssetKeyValue ON tblAssetContentMetadata (AssetId, MetadataKey, MetadataValue);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort. Which rows were once one comma-joined value is not recorded, so every asset
            // holding several values for a key is joined back into one row, whether or not those
            // values started out together.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblAssetContentMetadata') AND name = 'UQ_ContentMetadata_AssetKeyValue')
    DROP INDEX UQ_ContentMetadata_AssetKeyValue ON tblAssetContentMetadata;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblAssetContentMetadata') AND name = 'IX_ContentMetadata_AssetKey')
    DROP INDEX IX_ContentMetadata_AssetKey ON tblAssetContentMetadata;

UPDATE keeper
SET MetadataValue = joined.Value
FROM tblAssetContentMetadata keeper
JOIN (
    SELECT AssetId, MetadataKey, MIN(AssetContentMetadataId) AS KeeperId,
           STRING_AGG(MetadataValue, ',') WITHIN GROUP (ORDER BY MetadataValue) AS Value
    FROM tblAssetContentMetadata
    GROUP BY AssetId, MetadataKey
    HAVING COUNT(*) > 1
) joined ON joined.KeeperId = keeper.AssetContentMetadataId;

DELETE extra
FROM tblAssetContentMetadata extra
WHERE EXISTS (
    SELECT 1 FROM tblAssetContentMetadata keeper
    WHERE keeper.AssetId = extra.AssetId
      AND keeper.MetadataKey = extra.MetadataKey
      AND keeper.AssetContentMetadataId < extra.AssetContentMetadataId);

CREATE UNIQUE INDEX IX_ContentMetadata_AssetKey ON tblAssetContentMetadata (AssetId, MetadataKey);");
        }
    }
}
