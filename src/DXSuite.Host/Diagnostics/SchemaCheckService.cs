using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Host.Diagnostics;

/// <summary>
/// Startup check: for each enabled tenant, lists the tables the app's EF models expect but the connected database
/// does not have (e.g. counter allocations, counter templates, airline accounts). Pages that need a missing table
/// show "couldn't load" or an empty list rather than an error, so this writes the answer to the log instead.
/// Output goes to logs/missing-tables-&lt;tenant&gt;-&lt;context&gt;.txt, with a draft logs/missing-tables-...sql
/// (EF's CREATE script for just those tables) to review and run by hand. Read-only: it never changes the database.
/// Findings are also printed on the console with a [SCHEMA-CHECK] prefix.
/// </summary>
internal sealed class SchemaCheckService(ITenantStore tenants, ILogger<SchemaCheckService> logger) : BackgroundService
{
    private static void Say(string message) =>
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [SCHEMA-CHECK] {message}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken); // let the host finish starting
            foreach (var tenant in await tenants.GetAllEnabledAsync(stoppingToken))
            {
                if (!string.IsNullOrWhiteSpace(tenant.DirectorConnectionString))
                    await CheckAsync(tenant, "Director", new DbContextOptionsBuilder<DirectorDbContext>()
                        .UseSqlServer(tenant.DirectorConnectionString).Options, o => new DirectorDbContext(o), stoppingToken);
#if MODULE_AVIATION
                // Aviation falls back to the Director connection when it has none of its own.
                var aviation = !string.IsNullOrWhiteSpace(tenant.AviationConnectionString)
                    ? tenant.AviationConnectionString : tenant.DirectorConnectionString;
                if (!string.IsNullOrWhiteSpace(aviation))
                    await CheckAsync(tenant, "Aviation", new DbContextOptionsBuilder<DXSuite.Aviation.Entities.AviationDbContext>()
                        .UseSqlServer(aviation).Options, o => new DXSuite.Aviation.Entities.AviationDbContext(o), stoppingToken);
#endif
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema check could not run.");
        }
    }

    private async Task CheckAsync<TContext>(TenantRecord tenant, string name, DbContextOptions<TContext> options,
        Func<DbContextOptions<TContext>, TContext> create, CancellationToken ct) where TContext : DbContext
    {
        try
        {
            await using var db = create(options);
            var existing = (await db.Database
                .SqlQueryRaw<string>("SELECT SCHEMA_NAME(schema_id) + '.' + name AS [Value] FROM sys.tables")
                .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = db.Model.GetEntityTypes()
                .Where(t => t.GetTableName() is not null && t.GetViewName() is null && !t.IsOwned())
                .Select(t => (Schema: t.GetSchema() ?? "dbo", Table: t.GetTableName()!))
                .Distinct()
                .Where(t => !existing.Contains($"{t.Schema}.{t.Table}"))
                .OrderBy(t => t.Table)
                .ToList();

            var logs = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logs);
            var stem = Path.Combine(logs, $"missing-tables-{tenant.TenantId}-{name}");

            if (missing.Count == 0)
            {
                logger.LogInformation("Schema check: all {Name} tables for tenant '{Tenant}' exist.", name, tenant.TenantId);
                Say($"{name} tables for tenant '{tenant.TenantId}': all present.");
                File.Delete(stem + ".txt"); File.Delete(stem + ".sql");
                return;
            }

            logger.LogWarning("Schema check: {Count} {Name} table(s) missing for tenant '{Tenant}': {Tables}. See {File}.",
                missing.Count, name, tenant.TenantId, string.Join(", ", missing.Select(m => m.Table)), stem + ".txt");
            await File.WriteAllTextAsync(stem + ".txt",
                $"{DateTime.Now:u} - {name} tables the app expects but the database does not have (tenant '{tenant.TenantId}'):\r\n" +
                string.Join("\r\n", missing.Select(m => $"  {m.Schema}.{m.Table}")) + "\r\n", ct);

            // Draft DDL for just those tables: EF's full create script, keeping the batches that mention one of them.
            var script = db.Database.GenerateCreateScript();
            var wanted = missing.Select(m => $"[{m.Table}]").ToArray();
            var batches = script.Split(["\nGO\r\n", "\nGO\n", "\r\nGO\r\n"], StringSplitOptions.RemoveEmptyEntries)
                .Where(b => wanted.Any(w => b.Contains(w, StringComparison.OrdinalIgnoreCase)));
            await File.WriteAllTextAsync(stem + ".sql",
                $"-- DRAFT generated by EF from the app's model for the missing {name} tables. Review before running.\r\n" +
                string.Join("\r\nGO\r\n", batches) + "\r\nGO\r\n", ct);

            Say($"{name} tables for tenant '{tenant.TenantId}': {missing.Count} missing ({string.Join(", ", missing.Select(m => m.Table))}). Draft script: {stem}.sql");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema check for {Name} (tenant '{Tenant}') failed - is the database reachable with this connection string?", name, tenant.TenantId);
        }
    }
}
