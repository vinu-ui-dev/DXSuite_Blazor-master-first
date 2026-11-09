using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Lets background services check for optional columns before querying them, so a tenant
/// database on a different schema version skips that feature instead of throwing
/// "Invalid column name" on every cycle.
/// </summary>
internal static class SchemaProbe
{
    /// <summary>
    /// True when <paramref name="table"/>.<paramref name="column"/> exists. Non-SQL Server
    /// providers (the local SQLite mode builds tables straight from the model) always return true.
    /// </summary>
    public static async Task<bool> ColumnExistsAsync(
        object context, string table, string column, CancellationToken cancellationToken = default)
    {
        if (context is not DbContext db || !db.Database.IsSqlServer())
            return true;

        var result = await db.Database
            .SqlQueryRaw<int>(
                "SELECT CASE WHEN COL_LENGTH({0}, {1}) IS NULL THEN 0 ELSE 1 END AS [Value]",
                table, column)
            .ToListAsync(cancellationToken);

        return result.FirstOrDefault() == 1;
    }
}
