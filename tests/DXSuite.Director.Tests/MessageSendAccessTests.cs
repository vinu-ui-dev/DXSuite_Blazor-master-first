using DXSuite.Director.Services.Messaging;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// What a user may pick when sending a Message Type (ZONE-08), read from that type's Message access rules (ZONE-07)
/// over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageSendAccessTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageSendAccessTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private IMessageSendAccess Access => new MessageSendAccess(_db.Factory);

    [LocalDbFact]
    public async Task A_user_with_no_Message_access_rule_may_send_the_type_nowhere()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Bo);

        Assert.Empty(await Access.ZonesAsync(Ana, Paging, oneOff: false));
        Assert.Empty(await Access.DisplaysAsync(Ana, Paging, oneOff: false));
        Assert.True((await Access.ReachAsync(Ana, Paging, oneOff: false)).IsEmpty);
    }

    [LocalDbFact]
    public async Task A_rule_on_one_Display_Zone_offers_that_zone_and_in_one_display_mode_only_the_displays_inside_it()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);

        Assert.Equal([new SendableZone(GatesA, "Gates A")], await Access.ZonesAsync(Ana, Paging, oneOff: false));
        var locations = await Access.DisplaysAsync(Ana, Paging, oneOff: false);
        var location = Assert.Single(locations);
        Assert.Equal((Terminal1, "Terminal 1 Gates"), (location.LocationId, location.Name));
        Assert.Equal([new SendableDisplay(GateA1, "Gate A1"), new SendableDisplay(GateA2, "Gate A2")], location.Displays);

        var reach = await Access.ReachAsync(Ana, Paging, oneOff: false);
        Assert.False(reach.AnyDisplay);
        Assert.True(reach.CoversZones([GatesA]));
        Assert.False(reach.CoversZones([GatesA, CheckIn]));
    }

    [LocalDbFact]
    public async Task A_group_rule_reaches_the_groups_members_only()
    {
        await _db.AddRuleAsync(Paging, CheckIn, groupId: Rmu);

        Assert.Equal([CheckIn], (await Access.ZonesAsync(Bo, Paging, oneOff: false)).Select(z => z.ZoneId));
        Assert.Empty(await Access.ZonesAsync(Ana, Paging, oneOff: false));
    }

    [LocalDbFact]
    public async Task Rules_add_up_and_zones_come_in_Display_Zone_list_order()
    {
        await _db.AddRuleAsync(Paging, CheckIn, userId: Bo);
        await _db.AddRuleAsync(Paging, GatesA, groupId: Rmu);

        Assert.Equal([GatesA, CheckIn], (await Access.ZonesAsync(Bo, Paging, oneOff: false)).Select(z => z.ZoneId));
        Assert.Equal([Terminal3, Terminal1], (await Access.DisplaysAsync(Bo, Paging, oneOff: false)).Select(l => l.LocationId));
    }

    [LocalDbFact]
    public async Task A_rule_for_every_Display_Zone_offers_every_live_zone_and_any_display_in_one_display_mode()
    {
        await _db.AddRuleAsync(Paging, zoneId: null, userId: Ana);

        Assert.Equal([GatesA, CheckIn], (await Access.ZonesAsync(Ana, Paging, oneOff: false)).Select(z => z.ZoneId));
        var displays = (await Access.DisplaysAsync(Ana, Paging, oneOff: false)).SelectMany(l => l.Displays).Select(d => d.DisplayId);
        // The lounge is in no Display Zone and Check-in 2 only in a retired one; any display may still be picked.
        Assert.Equal([CheckIn1, CheckIn2, GateA1, GateA2, Lounge], displays);
        Assert.True((await Access.ReachAsync(Ana, Paging, oneOff: false)).AnyDisplay);
    }

    [LocalDbFact]
    public async Task A_one_off_Message_is_offered_only_through_rules_that_allow_one_off_text()
    {
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: true, userId: Ana);
        await _db.AddRuleAsync(Paging, CheckIn, canFreeText: false, userId: Ana);
        await _db.AddRuleAsync(Paging, zoneId: null, canFreeText: false, groupId: Rmu);

        Assert.Equal([GatesA, CheckIn], (await Access.ZonesAsync(Ana, Paging, oneOff: false)).Select(z => z.ZoneId));
        Assert.Equal([GatesA], (await Access.ZonesAsync(Ana, Paging, oneOff: true)).Select(z => z.ZoneId));
        Assert.Equal([GateA1, GateA2], (await Access.DisplaysAsync(Ana, Paging, oneOff: true)).SelectMany(l => l.Displays).Select(d => d.DisplayId));
        Assert.True((await Access.ReachAsync(Bo, Paging, oneOff: true)).IsEmpty);
        Assert.False((await Access.ReachAsync(Bo, Paging, oneOff: false)).IsEmpty);
    }

    [LocalDbFact]
    public async Task A_rule_on_a_retired_Display_Zone_grants_nothing()
    {
        await _db.AddRuleAsync(Paging, OldCheckIn, canFreeText: true, userId: Ana);

        Assert.Empty(await Access.ZonesAsync(Ana, Paging, oneOff: false));
        Assert.Empty(await Access.DisplaysAsync(Ana, Paging, oneOff: false));
        Assert.False((await Access.ReachAsync(Ana, Paging, oneOff: true)).CoversZones([OldCheckIn]));
    }

    [LocalDbFact]
    public async Task Admin_needs_no_Message_access_rule()
    {
        Assert.Equal([GatesA, CheckIn], (await Access.ZonesAsync(Admin, Paging, oneOff: true)).Select(z => z.ZoneId));
        Assert.Equal(5, (await Access.DisplaysAsync(Admin, Paging, oneOff: true)).Sum(l => l.Displays.Count));
        var reach = await Access.ReachAsync(Admin, Emergency, oneOff: true);
        Assert.True(reach.AnyDisplay);
        Assert.False(reach.CoversZones([OldCheckIn]));
    }

    [LocalDbFact]
    public async Task A_rule_covers_only_its_own_Message_Type()
    {
        await _db.AddRuleAsync(Emergency, zoneId: null, canFreeText: true, userId: Ana);

        Assert.True((await Access.ReachAsync(Ana, Paging, oneOff: false)).IsEmpty);
        Assert.False((await Access.ReachAsync(Ana, Emergency, oneOff: false)).IsEmpty);
    }
}
