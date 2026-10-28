using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Tests;

// Clause numbers refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>A Director database on LocalDB shared by the Display Zone tests; each test starts from no Display Zones.</summary>
public sealed class DisplayZoneDatabase : IAsyncLifetime
{
    public const int Terminal1 = 11;
    public const int Terminal3 = 13;
    public const int GateA1 = 101, GateA2 = 102, GateA3 = 103, CheckIn1 = 301, CheckIn2 = 302;

    private readonly string _database = $"DisplayZones_{Guid.NewGuid():N}"[..30];

    private string Connection =>
        LocalDb.Connection(_database);

    public DirectorDbContext Director() =>
        new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(Connection).Options);

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using var db = Director();
        await LocalDb.EnsureCreatedAsync(db.Database);

        // Locations, displays, Messages and grantees are seeded without their own parents; only the Display Zone
        // tables and the Activation target keys are under test.
        await db.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
            ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
            ALTER TABLE tblMessageActivation NOCHECK CONSTRAINT ALL;
            ALTER TABLE tblMessagePermission NOCHECK CONSTRAINT ALL;
            SET IDENTITY_INSERT tblLocation ON;
            INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, SortOrder, EnableShuffle, EnableFade, EnableContentApproval)
            VALUES ({Terminal3}, 1, 1, 'Terminal 3 Check-in', 1, 0, 1, 0), ({Terminal1}, 1, 1, 'Terminal 1 Gates', 2, 0, 1, 0);
            SET IDENTITY_INSERT tblLocation OFF;
            SET IDENTITY_INSERT tblDisplay ON;
            INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
            VALUES ({GateA2}, {Terminal1}, 'Gate A2', 0, 1, 1, 0, 0, 1, 0), ({GateA1}, {Terminal1}, 'Gate A1', 0, 1, 1, 0, 0, 1, 0),
                   ({GateA3}, {Terminal1}, 'Gate A3', 0, 1, 1, 0, 0, 1, 0),
                   ({CheckIn1}, {Terminal3}, 'Check-in 1', 0, 1, 1, 0, 0, 1, 0), ({CheckIn2}, {Terminal3}, 'Check-in 2', 0, 1, 1, 0, 0, 1, 0);
            SET IDENTITY_INSERT tblDisplay OFF;
            """);
    }

    public async Task ResetAsync()
    {
        await using var db = Director();
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM tblMessageActivationTarget; DELETE FROM tblMessageActivationZone; DELETE FROM tblMessageActivationVersion; DELETE FROM tblMessageActivation;
            DELETE FROM tblMessagePermission;
            DELETE FROM tblZoneDisplay; DELETE FROM tblZone;
            """);
    }

    public async Task DisposeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using var db = Director();
        await db.Database.EnsureDeletedAsync();
    }
}

[Trait("Category", "Integration")]
public sealed class DisplayZoneServiceTests(DisplayZoneDatabase database) : IClassFixture<DisplayZoneDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => LocalDbFactAttribute.IsAvailable ? database.ResetAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private readonly RecordingEventLog _events = new();

    private IDisplayZoneService Service(KeySession? session = null) =>
        new DisplayZoneService(new Factory(database.Director), session ?? new KeySession(PermissionKeys.ConfigTabZoneBuilder),
            new FixedClock(Now), _events, Microsoft.Extensions.Logging.Abstractions.NullLogger<DisplayZoneService>.Instance);

    private static DisplayZoneEdit Edit(string name, params int[] displayIds) =>
        new(null, name, null, displayIds.ToHashSet());

    // ZONE-11: saving compares the ticked displays with the stored rows.
    [LocalDbFact]
    public async Task Re_saving_a_Display_Zone_unchanged_writes_no_rows()
    {
        var service = Service();
        var created = await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1, DisplayZoneDatabase.GateA2));

        var again = await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1, DisplayZoneDatabase.GateA2) with { ZoneId = created.ZoneId });

        Assert.Equal(DisplayZoneSaveOutcome.Saved, again.Outcome);
        Assert.Equal(0, again.RowsWritten);
        Assert.Equal(new HashSet<int> { DisplayZoneDatabase.GateA1, DisplayZoneDatabase.GateA2 }, await service.DisplayIdsAsync(created.ZoneId));
    }

    [LocalDbFact]
    public async Task Saving_writes_only_the_displays_added_and_removed()
    {
        var service = Service();
        var created = await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1, DisplayZoneDatabase.GateA2));

        var changed = await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA2, DisplayZoneDatabase.GateA3) with { ZoneId = created.ZoneId });

        Assert.Equal(2, changed.RowsWritten);
        Assert.Equal(new HashSet<int> { DisplayZoneDatabase.GateA2, DisplayZoneDatabase.GateA3 }, await service.DisplayIdsAsync(created.ZoneId));
    }

    // ZONE-04: a Display Zone is its ZoneId; CoreAPI and past Activations follow it through a rename.
    [LocalDbFact]
    public async Task A_rename_changes_only_the_name_and_keeps_the_ZoneId()
    {
        var service = Service();
        var created = await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1));

        var renamed = await service.SaveAsync(Edit("Terminal 1 Gates A", DisplayZoneDatabase.GateA1) with { ZoneId = created.ZoneId });

        Assert.Equal(created.ZoneId, renamed.ZoneId);
        Assert.Equal(1, renamed.RowsWritten);
        var zone = Assert.Single(await service.ListAsync(includeRetired: false));
        Assert.Equal((created.ZoneId, "Terminal 1 Gates A", 1), (zone.ZoneId, zone.Name, zone.DisplayCount));
    }

    [LocalDbFact]
    public async Task A_name_is_required_and_unique_among_retired_Display_Zones_too()
    {
        var service = Service();
        var retired = await service.SaveAsync(Edit("Old Pier F"));
        await service.RetireAsync(retired.ZoneId);

        Assert.Equal(DisplayZoneSaveOutcome.NameRequired, (await service.SaveAsync(Edit("  "))).Outcome);
        Assert.Equal(DisplayZoneSaveOutcome.NameTaken, (await service.SaveAsync(Edit("Old Pier F "))).Outcome);
    }

    // ZONE-11: SortOrder is list order only; a new Display Zone joins the end.
    [LocalDbFact]
    public async Task Display_Zones_list_in_the_order_set_and_a_reorder_renumbers_them()
    {
        var service = Service();
        var a = (await service.SaveAsync(Edit("Gates A"))).ZoneId;
        var b = (await service.SaveAsync(Edit("Gates B"))).ZoneId;
        var c = (await service.SaveAsync(Edit("Check-in"))).ZoneId;

        await service.ReorderAsync([c, a, b]);

        Assert.Equal([c, a, b], (await service.ListAsync(includeRetired: false)).Select(z => z.ZoneId));
        await using var db = database.Director();
        Assert.Equal([1, 2, 3], await db.TblZones.OrderBy(z => z.SortOrder).Select(z => z.SortOrder).ToListAsync());
    }

    [LocalDbFact]
    public async Task A_reorder_of_the_visible_list_leaves_a_hidden_retired_Display_Zone_in_its_place()
    {
        var service = Service();
        var a = (await service.SaveAsync(Edit("Gates A"))).ZoneId;
        var retired = (await service.SaveAsync(Edit("Old Pier F"))).ZoneId;
        var b = (await service.SaveAsync(Edit("Gates B"))).ZoneId;
        await service.RetireAsync(retired);

        await service.ReorderAsync([b, a]);

        Assert.Equal([b, retired, a], (await service.ListAsync(includeRetired: true)).Select(z => z.ZoneId));
    }

    [Fact]
    public void The_order_puts_the_moved_zones_into_the_places_they_held()
    {
        Assert.Equal([3, 9, 1, 2], DisplayZoneOrder.Apply([1, 9, 2, 3], [3, 1, 2]));
        Assert.Equal([2, 1], DisplayZoneOrder.Apply([1, 2], [2, 1, 77]));
    }

    // ZONE-11: the editor's tree is every location with its displays.
    [LocalDbFact]
    public async Task The_location_tree_lists_each_location_with_its_displays()
    {
        var tree = await Service().LocationTreeAsync();

        Assert.Equal(["Terminal 3 Check-in", "Terminal 1 Gates"], tree.Select(l => l.Name));
        Assert.Equal(["Gate A1", "Gate A2", "Gate A3"], tree[1].Displays.Select(d => d.Name));
        Assert.Equal(DisplayZoneDatabase.GateA1, tree[1].Displays[0].DisplayId);
    }

    // ZONE-12: never targeted, so it may be deleted, and its Message access rules go with it.
    [LocalDbFact]
    public async Task A_Display_Zone_no_Activation_has_targeted_is_deleted_with_its_Message_access_rules()
    {
        var service = Service();
        var zone = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        var other = (await service.SaveAsync(Edit("Gates B"))).ZoneId;
        await AddRuleAsync(zone);
        await AddRuleAsync(other);

        Assert.Equal(DisplayZoneRemoval.Deleted, await service.DeleteAsync(zone));

        await using var db = database.Director();
        Assert.False(await db.TblZones.AnyAsync(z => z.ZoneId == zone));
        Assert.False(await db.TblZoneDisplays.AnyAsync(z => z.ZoneId == zone));
        Assert.Equal([other], await db.TblMessagePermissions.Select(p => p.ZoneId).ToListAsync());
    }

    [LocalDbFact]
    public async Task A_targeted_Display_Zone_can_only_be_retired_and_past_Activations_still_name_it()
    {
        var service = Service();
        var zone = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        await TargetAsync(zone, start: Now.AddDays(-2), end: Now.AddDays(-1));

        Assert.Equal(DisplayZoneRemoval.Targeted, await service.DeleteAsync(zone));
        Assert.Equal(DisplayZoneRemoval.Retired, await service.RetireAsync(zone));

        Assert.Empty(await service.ListAsync(includeRetired: false));
        var shown = Assert.Single(await service.ListAsync(includeRetired: true));
        Assert.Equal(Now, shown.RetiredUtc);
        Assert.True(shown.EverTargeted);
        await using var db = database.Director();
        var target = await db.TblMessageActivationTargets.Include(t => t.Zone).SingleAsync();
        Assert.Equal("Gates A", target.Zone!.ZoneName);
    }

    // A-33: "running" is active or scheduled.
    public static TheoryData<string, int, int?> Activations => new()
    {
        { "active until cancelled", -60, null },
        { "active with an end", -60, 60 },
        { "scheduled", 60, 120 },
    };

    [LocalDbTheory]
    [MemberData(nameof(Activations))]
    public async Task Neither_delete_nor_retire_works_while_an_active_or_scheduled_Activation_uses_the_Display_Zone(
        string activation, int startMinutes, int? endMinutes)
    {
        var service = Service();
        var zone = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        await TargetAsync(zone, Now.AddMinutes(startMinutes), endMinutes is { } m ? Now.AddMinutes(m) : null);

        Assert.Equal(DisplayZoneRemoval.InUse, await service.DeleteAsync(zone));
        Assert.Equal(DisplayZoneRemoval.InUse, await service.RetireAsync(zone));
        var listed = Assert.Single(await service.ListAsync(includeRetired: false));
        Assert.True(listed.InUse, activation);
    }

    // ZONE-06 gives a display one screen row, through the first picked zone that reaches it, so a zone whose displays
    // all sit in another picked zone has no row; the Activation still went through it (ZONE-12, A-33).
    [LocalDbFact]
    public async Task A_Display_Zone_picked_at_send_with_no_screen_row_of_its_own_is_held_and_then_only_retires()
    {
        var service = Service();
        var first = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        var hidden = (await service.SaveAsync(Edit("Gate A1 only", DisplayZoneDatabase.GateA1))).ZoneId;
        var activation = await TargetAsync(first, Now.AddMinutes(-60), end: Now.AddMinutes(60), alsoPicked: hidden);

        Assert.Equal(DisplayZoneRemoval.InUse, await service.DeleteAsync(hidden));
        Assert.Equal(DisplayZoneRemoval.InUse, await service.RetireAsync(hidden));
        var listed = (await service.ListAsync(includeRetired: false)).Single(z => z.ZoneId == hidden);
        Assert.True(listed.InUse && listed.EverTargeted);

        await using (var db = database.Director())
            await db.TblMessageActivations.Where(a => a.MessageActivationId == activation)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndUtc, Now.AddMinutes(-1)));

        Assert.Equal(DisplayZoneRemoval.Targeted, await service.DeleteAsync(hidden));
        Assert.Equal(DisplayZoneRemoval.Retired, await service.RetireAsync(hidden));
    }

    [LocalDbFact]
    public async Task A_cancelled_Activation_does_not_hold_a_Display_Zone()
    {
        var service = Service();
        var zone = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        await TargetAsync(zone, Now.AddMinutes(-60), end: null, cancelled: true);

        Assert.False(Assert.Single(await service.ListAsync(includeRetired: false)).InUse);
        Assert.Equal(DisplayZoneRemoval.Retired, await service.RetireAsync(zone));
    }

    [LocalDbFact]
    public async Task Changing_Display_Zones_needs_the_zone_builder_key()
    {
        var service = Service();
        var zone = (await service.SaveAsync(Edit("Gates A"))).ZoneId;
        var without = Service(new KeySession(PermissionKeys.PageMessages));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => without.SaveAsync(Edit("Gates B")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => without.ReorderAsync([zone]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => without.DeleteAsync(zone));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => without.RetireAsync(zone));
    }

    // B-53: each change logs its display-zone.* event (AUD-08).
    [LocalDbFact]
    public async Task Creating_a_Display_Zone_logs_created_with_name_description_and_order_and_the_displays_added()
    {
        var zone = (await Service().SaveAsync(new DisplayZoneEdit(null, "Gates A", "Pier A gates",
            new HashSet<int> { DisplayZoneDatabase.GateA2, DisplayZoneDatabase.GateA1 }))).ZoneId;

        var created = _events.Single(UserEventActionKeys.DisplayZoneCreated);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.DisplayZone, zone, "Gates A"), created.Target);
        Assert.Equal("""{"name":"Gates A","description":"Pier A gates","order":1}""", RecordingEventLog.DetailsJson(created));
        Assert.Equal($$"""{"added":[{{DisplayZoneDatabase.GateA1}},{{DisplayZoneDatabase.GateA2}}],"removed":[]}""",
            RecordingEventLog.DetailsJson(_events.Single(UserEventActionKeys.DisplayZoneMembershipChanged)));
    }

    [LocalDbFact]
    public async Task A_rename_logs_renamed_old_and_new_and_a_membership_change_logs_the_displays_added_and_removed()
    {
        var service = Service();
        var zone = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        while (_events.Written.TryDequeue(out _)) { }

        await service.SaveAsync(Edit("Pier A", DisplayZoneDatabase.GateA2) with { ZoneId = zone });
        await service.SaveAsync(Edit("Pier A", DisplayZoneDatabase.GateA2) with { ZoneId = zone });

        var renamed = _events.Single(UserEventActionKeys.DisplayZoneRenamed);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.DisplayZone, zone, "Pier A"), renamed.Target);
        Assert.Equal("""{"name":{"old":"Gates A","new":"Pier A"},"description":{"old":null,"new":null},"order":1}""",
            RecordingEventLog.DetailsJson(renamed));
        Assert.Equal($$"""{"added":[{{DisplayZoneDatabase.GateA2}}],"removed":[{{DisplayZoneDatabase.GateA1}}]}""",
            RecordingEventLog.DetailsJson(_events.Single(UserEventActionKeys.DisplayZoneMembershipChanged)));
    }

    [LocalDbFact]
    public async Task Retiring_and_deleting_log_their_events_and_a_refused_delete_logs_refused()
    {
        var service = Service();
        var targeted = (await service.SaveAsync(Edit("Gates A", DisplayZoneDatabase.GateA1))).ZoneId;
        var unused = (await service.SaveAsync(Edit("Check-in"))).ZoneId;
        await TargetAsync(targeted, start: Now.AddDays(-2), end: Now.AddDays(-1));

        await service.DeleteAsync(targeted);
        await service.RetireAsync(targeted);
        await service.DeleteAsync(unused);

        Assert.Equal([(UserEventOutcome.Refused, targeted.ToString()), (UserEventOutcome.Succeeded, unused.ToString())],
            _events.Of(UserEventActionKeys.DisplayZoneDeleted).Select(e => (e.Outcome, e.Target!.Id)));
        Assert.Equal("""{"name":"Check-in","description":null,"order":2}""",
            RecordingEventLog.DetailsJson(_events.Of(UserEventActionKeys.DisplayZoneDeleted)[1]));
        var retired = _events.Single(UserEventActionKeys.DisplayZoneRetired);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.DisplayZone, targeted, "Gates A"), retired.Target);
        Assert.Equal("""{"name":"Gates A","description":null,"order":1}""", RecordingEventLog.DetailsJson(retired));
    }

    [LocalDbFact]
    public async Task A_change_without_the_zone_builder_key_logs_refused()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(new KeySession(PermissionKeys.PageMessages)).SaveAsync(Edit("Gates B")));

        var refused = _events.Single(UserEventActionKeys.DisplayZoneCreated);
        Assert.Equal((UserEventOutcome.Refused, "Gates B"), (refused.Outcome, refused.Target!.Name));
    }

    private async Task AddRuleAsync(int zoneId)
    {
        await using var db = database.Director();
        db.TblMessagePermissions.Add(new TblMessagePermission { UserGroupId = 1, MessageTypeId = 1, ZoneId = zoneId });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// An Activation sent to <paramref name="zoneId"/>, whose screen row comes through it, and to
    /// <paramref name="alsoPicked"/> when given, which reaches no display of its own.
    /// </summary>
    private async Task<int> TargetAsync(int zoneId, DateTime start, DateTime? end, bool cancelled = false, int? alsoPicked = null)
    {
        await using var db = database.Director();
        var activation = new TblMessageActivation
        {
            MessageId = 1, StartUtc = start, EndUtc = end, ActivatedUtc = start,
            CancelledUtc = cancelled ? start.AddMinutes(1) : null,
        };
        activation.Targets.Add(new TblMessageActivationTarget
        {
            DisplayId = DisplayZoneDatabase.GateA1, ZoneId = zoneId, DisplayName = "Gate A1", LocationName = "Terminal 1",
        });
        foreach (var picked in alsoPicked is { } other ? [zoneId, other] : new[] { zoneId })
            activation.ChosenZones.Add(new TblMessageActivationZone { ZoneId = picked });
        db.TblMessageActivations.Add(activation);
        await db.SaveChangesAsync();
        return activation.MessageActivationId;
    }

    private sealed class Factory(Func<DirectorDbContext> create) : IDbContextFactory<DirectorDbContext>
    {
        public DirectorDbContext CreateDbContext() => create();
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
