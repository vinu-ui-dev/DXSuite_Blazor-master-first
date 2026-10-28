using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DXSuite.Director.Tests;

// Clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The product Message Type seeds (B-39, MSG-20): what the migrations leave in a new tenant's Director database, and
/// what they add to an existing tenant's without touching the types it already has. Nothing seeded is visible until
/// the tenant's Messaging flag is on; that gate is covered by MessagingAndFlightDataEditorGateTests and
/// TenantFeatureRouteGateTests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageTypeSeedTests : IClassFixture<MessageTypeSeedDatabases>
{
    private readonly MessageTypeSeedDatabases _db;

    public MessageTypeSeedTests(MessageTypeSeedDatabases db) => _db = db;

    // ---- A new tenant ----

    [LocalDbFact]
    public async Task A_new_tenant_has_the_four_product_types_in_the_seeded_rank_order()
    {
        await using var db = _db.NewTenant();
        var types = await LiveInRankOrder(db);

        Assert.Equal(["EMERGENCY", "IRROPS", "VISUAL_PAGING", "COUNTER"], types.Select(t => t.Code));
        Assert.Equal(["Emergency", "IRROPS", "Visual Paging", "Counter"], types.Select(t => t.DisplayName));
        Assert.Equal(types.Count, types.Select(t => t.Priority).Distinct().Count());
        Assert.DoesNotContain(types, t => t.IsTopPriority);
    }

    [LocalDbFact]
    public async Task Emergency_is_Full_screen_and_IRROPS_and_Visual_Paging_keep_their_Overlay()
    {
        await using var db = _db.NewTenant();
        var parts = await db.TblMessageTypes.ToDictionaryAsync(t => t.Code, t => t.DisplayMode);

        Assert.Equal(MessageDisplayMode.FullScreen, parts["EMERGENCY"]);
        Assert.Equal(MessageDisplayMode.Overlay, parts["IRROPS"]);
        Assert.Equal(MessageDisplayMode.Overlay, parts["VISUAL_PAGING"]);
    }

    [LocalDbFact]
    public async Task Every_seeded_type_is_written_in_English_then_French_with_the_default_character_limit()
    {
        await using var db = _db.NewTenant();
        var types = await db.TblMessageTypes.Include(t => t.Languages).ToListAsync();

        Assert.Equal(4, types.Count);
        Assert.All(types, t =>
        {
            Assert.Equal(["eng", "fra"], t.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode));
            Assert.Equal(160, t.MaxCharacters);
        });
    }

    // ---- An existing tenant ----

    [LocalDbFact]
    public async Task An_existing_tenant_keeps_its_types_and_gets_no_second_copy_of_any_seed()
    {
        await using var db = _db.ExistingTenant();
        var codes = await db.TblMessageTypes.Select(t => t.Code).ToListAsync();

        Assert.Equal(["COUNTER", "EMERGENCY", "GATE", "IRROPS", "VISUAL_PAGING"], codes.Order());
        Assert.Equal("Fire alarm", (await db.TblMessageTypes.SingleAsync(t => t.Code == "EMERGENCY")).DisplayName);
    }

    [LocalDbFact]
    public async Task A_type_deleted_on_an_existing_tenant_stays_deleted()
    {
        await using var db = _db.ExistingTenant();
        var paging = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.Code == "VISUAL_PAGING");

        Assert.True(paging.IsDeleted);
        Assert.Empty(paging.Languages);
    }

    [LocalDbFact]
    public async Task A_seed_missing_from_an_existing_tenant_joins_the_bottom_of_the_rank_order()
    {
        await using var db = _db.ExistingTenant();
        var types = await LiveInRankOrder(db);

        Assert.Equal(["EMERGENCY", "IRROPS", "GATE", "COUNTER"], types.Select(t => t.Code));
        var counter = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.Code == "COUNTER");
        Assert.Equal(MessageDisplayMode.Ticker, counter.DisplayMode);
        Assert.Equal(["eng", "fra"], counter.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode));
    }

    [LocalDbFact]
    public async Task Only_seeded_types_with_no_languages_get_English_and_French()
    {
        await using var db = _db.ExistingTenant();
        var languages = await db.TblMessageTypeLanguages
            .OrderBy(l => l.SortOrder)
            .Select(l => new { l.MessageType.Code, l.LanguageCode })
            .ToListAsync();

        Assert.Equal(["fra"], languages.Where(l => l.Code == "EMERGENCY").Select(l => l.LanguageCode));
        Assert.Equal(["eng", "fra"], languages.Where(l => l.Code == "IRROPS").Select(l => l.LanguageCode));
        Assert.Equal(["eng"], languages.Where(l => l.Code == "GATE").Select(l => l.LanguageCode));
    }

    private static Task<List<TblMessageType>> LiveInRankOrder(DirectorDbContext db) =>
        db.TblMessageTypes.Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.IsTopPriority).ThenBy(t => t.Priority).ThenBy(t => t.MessageTypeId)
            .ToListAsync();
}

/// <summary>
/// Two Director databases on LocalDB. The new tenant is migrated from empty to head. The existing tenant is migrated
/// to the migration before the seeds, given the state an admin could have left it in, then migrated to head.
/// </summary>
public sealed class MessageTypeSeedDatabases : IAsyncLifetime
{
    internal const string BeforeSeeds = "20261020062541_AddMessagingSchema";

    private readonly string _newTenant = $"MtSeedNew_{Guid.NewGuid():N}"[..30];
    private readonly string _existingTenant = $"MtSeedOld_{Guid.NewGuid():N}"[..30];

    public DirectorDbContext NewTenant() => Director(_newTenant);

    public DirectorDbContext ExistingTenant() => Director(_existingTenant);

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using (var db = NewTenant())
            await db.Database.MigrateAsync();

        await using (var db = ExistingTenant())
            await db.GetService<IMigrator>().MigrateAsync(BeforeSeeds);

        await using (var db = ExistingTenant())
            await db.Database.ExecuteSqlRawAsync(ExistingTenantRows);

        await using (var db = ExistingTenant())
            await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        foreach (var name in new[] { _newTenant, _existingTenant })
        {
            await using var db = Director(name);
            await db.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>
    /// Emergency renamed with French only, IRROPS with no languages (a tenant without Messages before B-30), Visual
    /// Paging deleted, Counter gone from the table, and a tenant type GATE ranked below the rest.
    /// </summary>
    private const string ExistingTenantRows = """
        UPDATE tblMessageType SET DisplayName = 'Fire alarm' WHERE Code = 'EMERGENCY';
        INSERT INTO tblMessageTypeLanguage (MessageTypeId, LanguageCode, SortOrder)
        SELECT MessageTypeId, 'fra', 1 FROM tblMessageType WHERE Code = 'EMERGENCY';
        UPDATE tblMessageType SET IsDeleted = 1 WHERE Code = 'VISUAL_PAGING';
        DELETE FROM tblMessageType WHERE Code = 'COUNTER';
        INSERT INTO tblMessageType (Code, DisplayName, Priority, DisplayMode, DefaultDisplaySeconds, DefaultHideSeconds, DefaultTextColor, DefaultBackgroundColor)
        VALUES ('GATE', 'Gate change', 60, 2, 10, 30, '#FFFFFF', '#000000');
        INSERT INTO tblMessageTypeLanguage (MessageTypeId, LanguageCode, SortOrder)
        SELECT MessageTypeId, 'eng', 1 FROM tblMessageType WHERE Code = 'GATE';
        """;

    private static DirectorDbContext Director(string database) =>
        new(new DbContextOptionsBuilder<DirectorDbContext>()
            .UseSqlServer(LocalDb.Connection(database))
            .Options);
}
