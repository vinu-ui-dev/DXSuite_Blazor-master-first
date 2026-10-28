using DXSuite.Director.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The frozen screen rows keep a deleted display's history (B-42: ZONE-06, AUD-04). The migration runs over a
/// database that already holds sent Activations, as one upgraded from the Messaging schema does.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ActivationTargetHistorySchemaTests : IClassFixture<ActivationTargetHistoryDatabase>
{
    private readonly ActivationTargetHistoryDatabase _db;

    public ActivationTargetHistorySchemaTests(ActivationTargetHistoryDatabase db) => _db = db;

    [LocalDbFact]
    public async Task Existing_screen_rows_take_the_display_and_location_names_they_have_at_upgrade()
    {
        await using var db = _db.Director();
        var rows = await db.TblMessageActivationTargets
            .Where(t => t.MessageActivationId == 1)
            .OrderBy(t => t.DisplayId)
            .Select(t => new { t.DisplayId, t.DisplayName, t.LocationName })
            .ToListAsync();

        // Location 20 has no name, so its display is placed as the send path places it.
        Assert.Equal(
            [(101, "T1 A1", "Terminal 1"), (102, "T1 A2", "Terminal 1"), (201, "T3 B1", "Location 20")],
            rows.Select(r => (r.DisplayId!.Value, r.DisplayName, r.LocationName)));
    }

    [LocalDbFact]
    public async Task Screen_rows_have_no_key_to_the_display()
    {
        await using var connection = new SqlConnection(_db.Connection);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID(N'tblMessageActivationTarget') AND referenced_object_id = OBJECT_ID(N'tblDisplay')
            """, connection);

        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }

    [LocalDbFact]
    public async Task A_display_can_be_deleted_under_its_past_screen_rows_and_they_still_name_it()
    {
        await using (var db = _db.Director())
            await db.TblDisplays.Where(d => d.DisplayId == 202).ExecuteDeleteAsync();

        await using var check = _db.Director();
        var row = await check.TblMessageActivationTargets.SingleAsync(t => t.MessageActivationId == 2);
        Assert.Equal((202, "T3 B2", "Location 20"), (row.DisplayId!.Value, row.DisplayName, row.LocationName));
    }

    [LocalDbFact]
    public async Task A_screen_row_must_name_its_display_and_location()
    {
        await using var connection = new SqlConnection(_db.Connection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "INSERT INTO tblMessageActivationTarget (MessageActivationId, DisplayId) VALUES (2, 101)", connection);

        await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }
}

/// <summary>
/// One Director database for the class. It stops before the migration that keeps the display names, seeds sent
/// Activations whose screen rows name displays only, then migrates to the head.
/// </summary>
public sealed class ActivationTargetHistoryDatabase : IAsyncLifetime
{
    internal const string BeforeDisplayNames = "20261020135946_AddApprovalChainEmail";

    private readonly string _database = $"TargetHistory_{Guid.NewGuid():N}"[..30];

    public string Connection =>
        LocalDb.Connection(_database);

    public DirectorDbContext Director() =>
        new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(Connection).Options);

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using (var db = Director())
            await db.GetService<IMigrator>().MigrateAsync(BeforeDisplayNames);

        await using (var db = Director())
            await db.Database.ExecuteSqlRawAsync(SentRows);

        await using (var db = Director())
            await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using var db = Director();
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>
    /// Location 10 "Terminal 1" (displays 101, 102) and location 20 with no name (displays 201, 202). Activation 1 went
    /// to Display Zone 1 (101, 102) and Zone 2 (201); Activation 2 went to display 202 alone.
    /// </summary>
    internal const string SentRows = """
        ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblLocation ON;
        INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, EnableShuffle, EnableFade, EnableContentApproval)
        VALUES (10, 1, 1, 'Terminal 1', 0, 0, 0), (20, 1, 1, NULL, 0, 0, 0);
        SET IDENTITY_INSERT tblLocation OFF;

        ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblDisplay ON;
        INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
        VALUES (101, 10, 'T1 A1', 1, 1, 1, 0, 0, 0, 0), (102, 10, 'T1 A2', 1, 1, 1, 0, 0, 0, 0), (201, 20, 'T3 B1', 1, 1, 1, 0, 0, 0, 0), (202, 20, 'T3 B2', 1, 1, 1, 0, 0, 0, 0);
        SET IDENTITY_INSERT tblDisplay OFF;

        SET IDENTITY_INSERT tblZone ON;
        INSERT INTO tblZone (ZoneId, ZoneName, SortOrder) VALUES (1, 'Gates A', 1), (2, 'Gates B', 2);
        SET IDENTITY_INSERT tblZone OFF;

        SET IDENTITY_INSERT tblMessage ON;
        INSERT INTO tblMessage (MessageId, MessageTypeId, Name, IsCanned, Status, IsDeleted) VALUES (1, 1, 'Fire', 1, 1, 0);
        SET IDENTITY_INSERT tblMessage OFF;

        SET IDENTITY_INSERT tblMessageActivation ON;
        INSERT INTO tblMessageActivation (MessageActivationId, MessageId, StartUtc, EndUtc, Source)
        VALUES (1, 1, '2026-10-01T10:00:00', '2026-10-01T11:00:00', 1), (2, 1, '2026-10-02T10:00:00', '2026-10-02T11:00:00', 1);
        SET IDENTITY_INSERT tblMessageActivation OFF;

        INSERT INTO tblMessageActivationTarget (MessageActivationId, ZoneId, DisplayId) VALUES
            (1, 1, 101), (1, 1, 102), (1, 2, 201),
            (2, NULL, 202);
        """;
}
