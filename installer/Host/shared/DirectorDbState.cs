using Microsoft.Data.SqlClient;

namespace DXSuite.Host.Setup;

/// <summary>
/// How a target Director database relates to EF Core migrations. Determined from two facts:
/// whether <c>__EFMigrationsHistory</c> exists, and whether user tables are present.
/// </summary>
public enum DirectorDbState
{
    /// <summary>No history, no user tables — Migrate() builds everything and records the baseline.</summary>
    Fresh,

    /// <summary>
    /// No history but user tables exist — a live pre-migrations DXSuite schema. Requires
    /// operator confirmation, then the baseline id is stamped before Migrate() runs.
    /// </summary>
    ExistingUntracked,

    /// <summary>History present — Migrate() applies only pending migrations.</summary>
    Tracked,
}

/// <summary>
/// State detection that needs only Microsoft.Data.SqlClient, so the wizard can decide
/// whether to prompt the operator without referencing the heavy DXSuite.Director assembly.
/// The DB tool reuses the same logic before provisioning.
/// </summary>
public static class DirectorDbDetector
{
    /// <summary>Inspect a connection already scoped to the target Director database.</summary>
    public static async Task<DirectorDbState> DetectAsync(string databaseConnectionString, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(databaseConnectionString);
        await cn.OpenAsync(ct);

        const string sql = @"
SELECT
  CASE WHEN OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL THEN 0 ELSE 1 END,
  (SELECT COUNT(*) FROM sys.tables
     WHERE is_ms_shipped = 0 AND name NOT IN ('__EFMigrationsHistory','sysdiagrams'))";

        await using var cmd = new SqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);

        var hasHistory = r.GetInt32(0) == 1;
        var userTables = r.GetInt32(1);

        if (hasHistory) return DirectorDbState.Tracked;
        return userTables > 0 ? DirectorDbState.ExistingUntracked : DirectorDbState.Fresh;
    }

    /// <summary>Does the target server already have a database with this name?</summary>
    public static async Task<bool> DatabaseExistsAsync(string masterConnectionString, string databaseName, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(masterConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DB_ID(@db)", cn);
        cmd.Parameters.AddWithValue("@db", databaseName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null && result != DBNull.Value;
    }

    /// <summary>Opens the connection to confirm reachability; returns (ok, message) for the UI.</summary>
    public static async Task<(bool Ok, string Message)> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT @@VERSION", cn);
            var version = (string?)await cmd.ExecuteScalarAsync(ct) ?? "";
            var firstLine = version.Split('\n').FirstOrDefault()?.Trim() ?? "Connected.";
            return (true, firstLine);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
