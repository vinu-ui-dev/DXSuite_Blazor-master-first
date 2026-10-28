using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DXSuite.Shared.Services;

/// <summary>
/// Local/offline escape hatch for every module's DbContext factory: when <c>Database:UseSqlite</c>
/// is true, each module opens a SQLite file under <c>Database:SqliteDirectory</c> (one file per
/// module name, so Design's identity-only context shares Director's file) instead of connecting to
/// SQL Server. Intended for a temporary local run with no reachable SQL Server — every connection
/// string, tenant record and module stays configured exactly as shipped; this only changes which
/// provider the already-resolved connection string is handed to.
/// </summary>
/// <remarks>
/// SQLite has none of SQL Server's <c>sp_getapplock</c>, computed columns or some of the error codes
/// a few services match on (e.g. unique-constraint races in <c>CoreApiFeedStateStore</c>), and this
/// product's EF Core migrations are SQL Server migrations. This mode never runs them — it builds each
/// database's schema straight from the current model with <c>EnsureCreated()</c> at startup — so it is
/// for exploring the UI locally, not a substitute for a real SQL Server dev database.
/// </remarks>
public static class LocalSqliteProvider
{
    public static bool IsEnabled(IConfiguration config) => config.GetValue<bool>("Database:UseSqlite");

    /// <summary>
    /// Configures <paramref name="options"/> for a per-module SQLite file when local SQLite mode is on;
    /// otherwise this is exactly <c>options.UseSqlServer(sqlServerConnStr)</c>, unchanged from before this
    /// escape hatch existed.
    /// </summary>
    /// <param name="moduleName">
    /// The SQLite file's base name (no extension). Use the same name for two contexts that must share one
    /// physical database (e.g. Design's identity-only context uses "Director", the same as Director's own
    /// context), so EnsureCreated only needs to run once per file.
    /// </param>
    public static DbContextOptionsBuilder UseConfiguredProvider(
        this DbContextOptionsBuilder options, IConfiguration config, string moduleName, string? sqlServerConnStr)
    {
        if (IsEnabled(config))
        {
            var dir = config["Database:SqliteDirectory"] ?? "App_Data/local-sqlite";
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{moduleName}.db");
            return options.UseSqlite($"Data Source={path}");
        }

        return options.UseSqlServer(sqlServerConnStr);
    }
}
