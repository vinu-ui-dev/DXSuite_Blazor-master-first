namespace DXSuite.Director.Migrations
{
    /// <summary>
    /// Catalog-guarded DDL for the metadata tables, where the EF model is not a reliable description
    /// of the database: index names differ between the model and deployed databases, and some
    /// uniqueness is enforced by a UNIQUE constraint rather than a unique index. A name-based drop
    /// silently misses the other spelling, and <c>DROP INDEX</c> fails against a constraint, so
    /// uniqueness is found by its columns instead.
    ///
    /// Applied migrations depend on this text, so change it only by adding members.
    /// </summary>
    internal static class MetadataIndexSql
    {
        /// <summary>
        /// Drops every unique index or UNIQUE constraint on <paramref name="table"/> whose key is exactly
        /// <paramref name="columns"/>, in any order, whatever it is named. The primary key is never touched.
        /// </summary>
        public static string DropUniqueOnColumns(string table, params string[] columns)
        {
            var columnList = string.Join(", ", columns.Select(c => $"'{c}'"));

            // Named per table: a migration script can emit several of these into one batch.
            var variable = "@drop_" + table;
            return $@"
DECLARE {variable} nvarchar(max) = N'';
SELECT {variable} = {variable} +
    CASE WHEN i.is_unique_constraint = 1
        THEN N'ALTER TABLE {table} DROP CONSTRAINT ' + QUOTENAME(i.name) + N'; '
        ELSE N'DROP INDEX ' + QUOTENAME(i.name) + N' ON {table}; '
    END
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('{table}')
  AND i.is_unique = 1
  AND i.is_primary_key = 0
  AND (SELECT COUNT(*) FROM sys.index_columns ic
       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) = {columns.Length}
  AND (SELECT COUNT(*) FROM sys.index_columns ic
       JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
         AND c.name IN ({columnList})) = {columns.Length};
IF {variable} <> N'' EXEC sp_executesql {variable};";
        }
    }
}
