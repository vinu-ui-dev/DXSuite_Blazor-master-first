using DXSuite.Director.ConfigTabs;
using DXSuite.Director.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

// Clause numbers refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Configuration > Display Zones (ZONE-11, ZONE-12) rendered for real over an in-memory <see cref="IDisplayZoneService"/>:
/// the list, its order, the delete or retire choice and the location tree editor.
/// </summary>
public class DisplayZonesTabTests
{
    private const int Terminal1 = 11, Terminal3 = 13;
    private const int GateA1 = 101, GateA2 = 102, GateA3 = 103, CheckIn1 = 301;

    [Fact]
    public async Task The_list_hides_retired_Display_Zones_until_Show_retired_is_ticked()
    {
        var zones = new FakeZones(Zone(1, "Gates A"), Zone(2, "Old Pier F", retired: true), Zone(3, "Check-in"));
        var tab = await RenderAsync(zones);

        Assert.Contains("Gates A", tab.Html);
        Assert.DoesNotContain("Old Pier F", tab.Html);

        await tab.ChangeAsync("dz-show-retired", true);

        Assert.Contains("Old Pier F", tab.Html);
        Assert.Contains("Retired", tab.Html);
    }

    // ZONE-12: never targeted means delete; targeted means retire only.
    [Fact]
    public async Task A_Display_Zone_offers_delete_until_it_has_been_targeted_and_retire_after()
    {
        var zones = new FakeZones(Zone(1, "Gates A"), Zone(2, "Gates B", targeted: true));
        var tab = await RenderAsync(zones);

        Assert.Contains("id=\"dz-delete-1\"", tab.Html);
        Assert.DoesNotContain("id=\"dz-retire-1\"", tab.Html);
        Assert.Contains("id=\"dz-retire-2\"", tab.Html);
        Assert.DoesNotContain("id=\"dz-delete-2\"", tab.Html);
    }

    [Fact]
    public async Task Deleting_asks_first_then_removes_the_Display_Zone_and_its_Message_access_rules()
    {
        var zones = new FakeZones(Zone(1, "Gates A"), Zone(2, "Gates B"));
        var tab = await RenderAsync(zones);

        await tab.ClickAsync("#dz-delete-1");
        Assert.Contains("Message access rules", tab.Html);
        Assert.Empty(zones.Deleted);

        await tab.ClickAsync("dz-confirm");

        Assert.Equal([1], zones.Deleted);
        Assert.DoesNotContain("Gates A", tab.Html);
    }

    [Fact]
    public async Task Retiring_asks_first_then_the_Display_Zone_leaves_the_list()
    {
        var zones = new FakeZones(Zone(1, "Gates A", targeted: true));
        var tab = await RenderAsync(zones);

        await tab.ClickAsync("#dz-retire-1");
        await tab.ClickAsync("dz-confirm");

        Assert.Equal([1], zones.Retired);
        Assert.DoesNotContain("Gates A", tab.Html);
    }

    // A-33: an active or scheduled Activation holds the Display Zone.
    [Fact]
    public async Task A_Display_Zone_in_use_can_be_neither_deleted_nor_retired()
    {
        var zones = new FakeZones(Zone(1, "Gates A", inUse: true), Zone(2, "Gates B", targeted: true, inUse: true));
        var tab = await RenderAsync(zones);

        Assert.Contains("id=\"dz-delete-1\" disabled", tab.Html);
        Assert.Contains("id=\"dz-retire-2\" disabled", tab.Html);
        Assert.Contains("active or scheduled", tab.Html);
    }

    [Fact]
    public async Task A_refusal_from_the_service_is_shown()
    {
        var zones = new FakeZones(Zone(1, "Gates A")) { Answer = DisplayZoneRemoval.InUse };
        var tab = await RenderAsync(zones);

        await tab.ClickAsync("#dz-delete-1");
        await tab.ClickAsync("dz-confirm");

        Assert.Contains("Gates A", tab.Html);
        Assert.Contains("active or scheduled", tab.Html);
    }

    // ZONE-11: order is list order, set with Move up, Move down or a drag.
    [Fact]
    public async Task Move_up_and_Move_down_save_the_new_list_order()
    {
        var zones = new FakeZones(Zone(1, "Gates A"), Zone(2, "Gates B"), Zone(3, "Check-in"));
        var tab = await RenderAsync(zones);

        await tab.ClickAsync("#dz-up-3");
        Assert.Equal([1, 3, 2], zones.Order);

        await tab.ClickAsync("#dz-down-1");
        Assert.Equal([3, 1, 2], zones.Order);
        Assert.True(tab.Html.IndexOf("Check-in", StringComparison.Ordinal) < tab.Html.IndexOf("Gates A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dragging_a_Display_Zone_onto_another_puts_it_in_that_place()
    {
        var zones = new FakeZones(Zone(1, "Gates A"), Zone(2, "Gates B"), Zone(3, "Check-in"));
        var tab = await RenderAsync(zones);

        await tab.DragAsync("#dz-row-3", "#dz-row-1");

        Assert.Equal([3, 1, 2], zones.Order);
    }

    // ZONE-11: one location tree with "x of y in this Display Zone" and "Remove x".
    [Fact]
    public async Task The_editor_shows_each_location_with_how_many_of_its_displays_are_in_the_Display_Zone()
    {
        var zones = new FakeZones(Zone(1, "Gates A")) { Members = { [1] = [GateA2] } };
        var tab = await RenderAsync(zones);

        await tab.ClickAsync("#dz-edit-1");

        Assert.Contains("Terminal 1 Gates", tab.Html);
        Assert.Contains("1 of 3 in this Display Zone", tab.Html);
        Assert.Contains("0 of 1 in this Display Zone", tab.Html);
        Assert.Contains("Remove 1", tab.Html);
    }

    // ZONE-02: ticking a location adds the displays in it at that moment.
    [Fact]
    public async Task Ticking_a_location_adds_its_displays_and_Remove_takes_them_out()
    {
        var zones = new FakeZones(Zone(1, "Gates A")) { Members = { [1] = [GateA2] } };
        var tab = await RenderAsync(zones);
        await tab.ClickAsync("#dz-edit-1");

        await tab.ChangeAsync($"#dz-location-{Terminal1}", true);
        Assert.Contains("3 of 3 in this Display Zone", tab.Html);

        await tab.ClickAsync($"#dz-remove-{Terminal1}");
        Assert.Contains("0 of 3 in this Display Zone", tab.Html);

        await tab.ChangeAsync($"#dz-location-{Terminal3}", true);
        await tab.ClickAsync("dz-save");

        var saved = Assert.Single(zones.Saved);
        Assert.Equal((1, "Gates A"), (saved.ZoneId, saved.Name));
        Assert.Equal(new HashSet<int> { CheckIn1 }, saved.DisplayIds);
    }

    [Fact]
    public async Task A_display_is_ticked_on_its_own_under_its_location()
    {
        var zones = new FakeZones();
        var tab = await RenderAsync(zones);
        await tab.ClickAsync("dz-add");

        await tab.ClickAsync($"#dz-expand-{Terminal1}");
        await tab.ChangeAsync($"#dz-display-{GateA3}", true);
        await tab.ChangeAsync("dz-name", "Gate A3 only");
        await tab.ClickAsync("dz-save");

        var saved = Assert.Single(zones.Saved);
        Assert.Equal((null, "Gate A3 only"), (saved.ZoneId, saved.Name));
        Assert.Equal(new HashSet<int> { GateA3 }, saved.DisplayIds);
        Assert.Contains("Gate A3 only", tab.Html);
    }

    [Fact]
    public async Task A_name_already_used_keeps_the_editor_open_with_the_reason()
    {
        var zones = new FakeZones(Zone(1, "Gates A")) { SaveAnswer = DisplayZoneSaveOutcome.NameTaken };
        var tab = await RenderAsync(zones);
        await tab.ClickAsync("dz-add");

        await tab.ChangeAsync("dz-name", "Gates A");
        await tab.ClickAsync("dz-save");

        Assert.Contains("already", tab.Html);
        Assert.Contains("dz-save", tab.Html);
    }

    private static DisplayZoneSummary Zone(int id, string name, bool retired = false, bool targeted = false, bool inUse = false) =>
        new(id, name, null, 0, retired ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : null, targeted, inUse);

    private static Task<InteractiveRenderer> RenderAsync(FakeZones zones)
    {
        var services = new ServiceCollection().AddSingleton<IDisplayZoneService>(zones).BuildServiceProvider();
        return InteractiveRenderer.RenderAsync<DisplayZonesTab>(new Dictionary<string, object?>(), services);
    }

    private sealed class FakeZones(params DisplayZoneSummary[] zones) : IDisplayZoneService
    {
        private readonly List<DisplayZoneSummary> _zones = zones.ToList();

        public Dictionary<int, int[]> Members { get; } = new();
        public List<int> Deleted { get; } = new();
        public List<int> Retired { get; } = new();
        public List<DisplayZoneEdit> Saved { get; } = new();
        public DisplayZoneRemoval? Answer { get; init; }
        public DisplayZoneSaveOutcome SaveAnswer { get; init; } = DisplayZoneSaveOutcome.Saved;

        public IReadOnlyList<int> Order => _zones.Select(z => z.ZoneId).ToList();

        public Task<IReadOnlyList<DisplayZoneSummary>> ListAsync(bool includeRetired, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DisplayZoneSummary>>(_zones.Where(z => includeRetired || !z.IsRetired).ToList());

        public Task<IReadOnlyList<DisplayZoneLocation>> LocationTreeAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DisplayZoneLocation>>(
            [
                new(Terminal1, "Terminal 1 Gates", [new(GateA1, "Gate A1"), new(GateA2, "Gate A2"), new(GateA3, "Gate A3")]),
                new(Terminal3, "Terminal 3 Check-in", [new(CheckIn1, "Check-in 1")]),
            ]);

        public Task<IReadOnlySet<int>> DisplayIdsAsync(int zoneId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<int>>((Members.GetValueOrDefault(zoneId) ?? []).ToHashSet());

        public Task<DisplayZoneSaveResult> SaveAsync(DisplayZoneEdit edit, CancellationToken ct = default)
        {
            if (SaveAnswer != DisplayZoneSaveOutcome.Saved) return Task.FromResult(new DisplayZoneSaveResult(SaveAnswer));
            Saved.Add(edit);
            var id = edit.ZoneId ?? _zones.Select(z => z.ZoneId).DefaultIfEmpty().Max() + 1;
            var summary = new DisplayZoneSummary(id, edit.Name, edit.Description, edit.DisplayIds.Count, null, false, false);
            var at = _zones.FindIndex(z => z.ZoneId == id);
            if (at < 0) _zones.Add(summary); else _zones[at] = summary;
            return Task.FromResult(new DisplayZoneSaveResult(DisplayZoneSaveOutcome.Saved, id, 1));
        }

        public Task ReorderAsync(IReadOnlyList<int> zoneIds, CancellationToken ct = default)
        {
            var order = DisplayZoneOrder.Apply(Order, zoneIds);
            var byId = _zones.ToDictionary(z => z.ZoneId);
            _zones.Clear();
            _zones.AddRange(order.Select(id => byId[id]));
            return Task.CompletedTask;
        }

        public Task<DisplayZoneRemoval> DeleteAsync(int zoneId, CancellationToken ct = default)
        {
            if (Answer is { } refused) return Task.FromResult(refused);
            Deleted.Add(zoneId);
            _zones.RemoveAll(z => z.ZoneId == zoneId);
            return Task.FromResult(DisplayZoneRemoval.Deleted);
        }

        public Task<DisplayZoneRemoval> RetireAsync(int zoneId, CancellationToken ct = default)
        {
            if (Answer is { } refused) return Task.FromResult(refused);
            Retired.Add(zoneId);
            var at = _zones.FindIndex(z => z.ZoneId == zoneId);
            _zones[at] = _zones[at] with { RetiredUtc = DateTime.UtcNow };
            return Task.FromResult(DisplayZoneRemoval.Retired);
        }
    }
}
