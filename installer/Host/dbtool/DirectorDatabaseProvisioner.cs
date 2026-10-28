using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Host.Setup;

/// <summary>Result of a provisioning run, surfaced to the operator/UI.</summary>
public sealed record ProvisionOutcome(
    DirectorDbState DetectedState,
    bool BaselineStamped,
    bool NeedsOperatorConfirmation);

/// <summary>
/// Brings a single on-prem Director database to the current schema using EF Core migrations
/// compiled into <see cref="DXSuite.Director"/>. Implements the three-step contract:
/// bootstrap the database, apply migrations, run the idempotent seed.
///
/// The baseline migration id is read at runtime (never hardcoded) and an existing untracked
/// schema is only stamped after explicit operator confirmation. See
/// <c>db/schema-migration-strategy.md</c> for the rationale.
/// </summary>
public sealed class DirectorDatabaseProvisioner
{
    private const string ProductVersion = "10.0.7"; // matches Microsoft.EntityFrameworkCore.SqlServer

    /// <param name="adminConnectionString">
    /// Account with CREATE DATABASE on the server and db_owner on the target (Migrate runs
    /// DDL). InitialCatalog is retargeted per step.
    /// </param>
    /// <param name="databaseName">The Director database name.</param>
    /// <param name="seedScriptText">Contents of db/scripts/director-seed.sql, shipped with the installer.</param>
    /// <param name="operatorConfirmedExistingBaseline">
    /// True only when the operator deliberately confirmed an existing schema matches the baseline.
    /// </param>
    public async Task<ProvisionOutcome> ProvisionAsync(
        string adminConnectionString,
        string databaseName,
        string seedScriptText,
        bool operatorConfirmedExistingBaseline,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(adminConnectionString, databaseName, log, ct);

        var dbConn = ConnectionStringFactory.Retarget(adminConnectionString, databaseName);
        var state = await DirectorDbDetector.DetectAsync(dbConn, ct);
        log?.Report($"Director DB '{databaseName}' state: {state}");

        await using var context = CreateContext(dbConn);

        // InitialCreate; read at runtime so this never drifts from the assembly.
        var baselineId = context.Database.GetMigrations().First();

        var stamped = false;
        if (state == DirectorDbState.ExistingUntracked)
        {
            if (!operatorConfirmedExistingBaseline)
                return new ProvisionOutcome(state, BaselineStamped: false, NeedsOperatorConfirmation: true);

            await StampBaselineAsync(dbConn, baselineId, ct);
            stamped = true;
            log?.Report($"Stamped existing schema with baseline {baselineId}.");
        }

        await context.Database.MigrateAsync(ct); // Fresh: builds all; otherwise applies pending
        log?.Report("Migrations applied.");

        if (!string.IsNullOrWhiteSpace(seedScriptText))
        {
            await RunSqlScriptAsync(dbConn, seedScriptText, ct);
            log?.Report("Seed applied.");
        }

        return new ProvisionOutcome(state, stamped, NeedsOperatorConfirmation: false);
    }

    /// <summary>Bootstrap: create the database with the required collation + recovery model if absent.</summary>
    private static async Task EnsureDatabaseAsync(string adminConn, string db, IProgress<string>? log, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionStringFactory.Retarget(adminConn, "master"));
        await cn.OpenAsync(ct);

        const string sql = @"
IF DB_ID(@db) IS NULL
BEGIN
    EXEC(N'CREATE DATABASE ' + QUOTENAME(@db) + N' COLLATE SQL_Latin1_General_CP1_CI_AS;');
    EXEC(N'ALTER DATABASE '  + QUOTENAME(@db) + N' SET RECOVERY SIMPLE;');
END";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@db", db);
        await cmd.ExecuteNonQueryAsync(ct);
        log?.Report($"Database '{db}' ensured.");
    }

    /// <summary>Record the baseline migration id so Migrate() treats the existing schema as applied.</summary>
    private static async Task StampBaselineAsync(string dbConn, string migrationId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(dbConn);
        await cn.OpenAsync(ct);

        const string sql = @"
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
    CREATE TABLE [__EFMigrationsHistory](
        [MigrationId] nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,
        [ProductVersion] nvarchar(32) NOT NULL);
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = @id)
    INSERT INTO [__EFMigrationsHistory]([MigrationId],[ProductVersion]) VALUES (@id, @ver);";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@id", migrationId);
        cmd.Parameters.AddWithValue("@ver", ProductVersion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DirectorDbContext CreateContext(string dbConn) =>
        new(new DbContextOptionsBuilder<DirectorDbContext>()
            .UseSqlServer(dbConn, o => o.MigrationsAssembly(typeof(DirectorDbContext).Assembly.GetName().Name))
            .Options);

    /// <summary>
    /// Runs a multi-batch T-SQL script (splits on GO). Skips USE batches so a custom database
    /// name is honored via the connection's InitialCatalog rather than the script's literal.
    /// </summary>
    private static async Task RunSqlScriptAsync(string dbConn, string script, CancellationToken ct)
    {
        var batches = Regex.Split(script, @"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        await using var cn = new SqlConnection(dbConn);
        await cn.OpenAsync(ct);

        foreach (var batch in batches)
        {
            var text = batch.Trim();
            if (text.Length == 0 || Regex.IsMatch(text, @"^\s*USE\s+", RegexOptions.IgnoreCase))
                continue;

            await using var cmd = new SqlCommand(text, cn) { CommandTimeout = 180 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
