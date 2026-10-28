using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DXSuite.Director.Tests;

/// <summary>
/// FDE-03 (B-22): CoreAPI holds the flight Outage mode switch and Director never stores it, so the migration drops
/// the copy the ported editor kept as the <c>ToggleOverride</c> row of <c>tblParameter</c> and leaves the rest.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FlightOverrideToggleRowTests
{
    private const string Migration = "RemoveFlightOverrideToggleParameter";

    [LocalDbFact]
    public async Task The_migration_removes_the_ToggleOverride_row_and_keeps_other_parameters()
    {
        var database = $"FdeToggle_{Guid.NewGuid():N}"[..30];
        var connection = LocalDb.Connection(database);
        DirectorDbContext Director() => new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(connection).Options);

        try
        {
            await using (var db = Director())
            {
                var migrations = db.Database.GetMigrations().ToList();
                var before = migrations[migrations.FindIndex(m => m.EndsWith("_" + Migration)) - 1];
                await db.GetService<IMigrator>().MigrateAsync(before);
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO tblParameter (KeyName, Value, CreateDate) VALUES ('ToggleOverride', '1', SYSDATETIME()), ('SomethingElse', 'kept', SYSDATETIME());
                    """);
            }

            await using (var db = Director())
                await db.Database.MigrateAsync();

            await using (var db = Director())
            {
                var keys = await db.TblParameters.Select(p => p.KeyName).ToListAsync();
                Assert.DoesNotContain("ToggleOverride", keys);
                Assert.Contains("SomethingElse", keys);
            }
        }
        finally
        {
            await using var db = Director();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
