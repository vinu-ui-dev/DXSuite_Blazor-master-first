using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;

namespace DXSuite.Director.Tests;

// Clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-53: Configuration > Message Types logs <c>message-type.*</c> with the fields that are not Business Rules, and one
/// <c>business-rule.changed</c> per Business Rule value DXME-447 catalogues (rank order, display and hide seconds,
/// character limit), keyed as the 1.2 Business Rules Editor keys them (BRE-04). Runs against LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageTypeEventsTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly RecordingEventLog _events = new(7);

    public MessageTypeEventsTests(MessageTypeDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private MessageTypeConfiguration Service() => _db.Service(userId: 7, events: _events);

    [LocalDbFact]
    public async Task A_new_type_logs_its_fields_its_Business_Rule_values_from_nothing_and_the_order_it_joins()
    {
        await _db.AddTypeAsync("EMERGENCY", priority: 1);
        var service = Service();
        var draft = service.NewDraft();
        draft.Code = "paging";
        draft.DisplayName = "Visual Paging";
        draft.ScreenPart = MessageDisplayMode.Overlay;
        draft.Languages.AddRange(["eng", "fra"]);

        var saved = await service.SaveAsync(draft, moveTopPriority: false, default);

        var target = new UserEventTarget(UserEventTargetTypes.MessageType, saved.MessageTypeId!.Value, "Visual Paging");
        var created = _events.Single(UserEventActionKeys.MessageTypeCreated);
        Assert.Equal(target, created.Target);
        Assert.Equal(
            """{"code":{"old":null,"new":"PAGING"},"displayName":{"old":null,"new":"Visual Paging"},"screenPart":{"old":null,"new":"Overlay"},"textColour":{"old":null,"new":"#FFFFFF"},"backgroundColour":{"old":null,"new":"#000000"},"languages":{"old":null,"new":["eng","fra"]},"slots":{"old":null,"new":[]}}""",
            RecordingEventLog.DetailsJson(created));
        Assert.Equal(
        [
            """{"key":"message-type.PAGING.display-seconds","old":null,"new":10,"unit":"seconds"}""",
            """{"key":"message-type.PAGING.hide-seconds","old":null,"new":30,"unit":"seconds"}""",
            """{"key":"message-type.PAGING.max-characters","old":null,"new":160,"unit":"characters"}""",
            """{"key":"message-types.order","old":["EMERGENCY"],"new":["EMERGENCY","PAGING"],"unit":"order"}""",
        ], _events.Of(UserEventActionKeys.BusinessRuleChanged).Select(RecordingEventLog.DetailsJson));
        Assert.All(_events.Of(UserEventActionKeys.BusinessRuleChanged), e => Assert.Equal(target, e.Target));
    }

    [LocalDbFact]
    public async Task An_edit_logs_only_the_changed_fields_and_one_business_rule_changed_per_changed_value()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var service = Service();
        var draft = (await service.OpenAsync(type, default))!;
        draft.DisplayName = "Visual Paging";
        draft.DisplaySeconds = 12;
        draft.MaxCharacters = 200;

        await service.SaveAsync(draft, moveTopPriority: false, default);

        Assert.Equal("""{"displayName":{"old":"PAGING name","new":"Visual Paging"}}""",
            RecordingEventLog.DetailsJson(_events.Single(UserEventActionKeys.MessageTypeEdited)));
        Assert.Equal(
        [
            """{"key":"message-type.PAGING.display-seconds","old":10,"new":12,"unit":"seconds"}""",
            """{"key":"message-type.PAGING.max-characters","old":160,"new":200,"unit":"characters"}""",
        ], _events.Of(UserEventActionKeys.BusinessRuleChanged).Select(RecordingEventLog.DetailsJson));
        Assert.Empty(_events.Of(UserEventActionKeys.MessageTypeTopPriorityMoved));
    }

    [LocalDbFact]
    public async Task Moving_Top_priority_logs_from_and_to_and_the_new_rank_order()
    {
        await _db.AddTypeAsync("HOLDER", priority: 1, top: true);
        await _db.AddTypeAsync("A", priority: 2);
        var other = await _db.AddTypeAsync("OTHER", priority: 3);
        var service = Service();
        var draft = (await service.OpenAsync(other, default))!;
        draft.IsTopPriority = true;

        await service.SaveAsync(draft, moveTopPriority: true, default);

        var moved = _events.Single(UserEventActionKeys.MessageTypeTopPriorityMoved);
        Assert.Equal(other.ToString(), moved.Target!.Id);
        Assert.Equal("""{"from":"HOLDER","to":"OTHER"}""", RecordingEventLog.DetailsJson(moved));
        Assert.Equal("""{"key":"message-types.order","old":["HOLDER","A","OTHER"],"new":["OTHER","HOLDER","A"],"unit":"order"}""",
            RecordingEventLog.DetailsJson(_events.Single(UserEventActionKeys.BusinessRuleChanged)));
        Assert.Equal("{}", RecordingEventLog.DetailsJson(_events.Single(UserEventActionKeys.MessageTypeEdited)));
    }

    [LocalDbFact]
    public async Task A_reorder_logs_the_old_and_new_order_by_code()
    {
        await _db.AddTypeAsync("A", priority: 10);
        await _db.AddTypeAsync("B", priority: 20);
        var c = await _db.AddTypeAsync("C", priority: 40);

        await Service().MoveAsync(c, 0, default);

        var reorder = _events.Single(UserEventActionKeys.BusinessRuleChanged);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.MessageType, c, "C name"), reorder.Target);
        Assert.Equal("""{"key":"message-types.order","old":["A","B","C"],"new":["C","A","B"],"unit":"order"}""",
            RecordingEventLog.DetailsJson(reorder));
    }

    [LocalDbFact]
    public async Task A_delete_logs_message_type_deleted_and_the_order_it_leaves_and_a_refused_delete_logs_refused()
    {
        var a = await _db.AddTypeAsync("A", priority: 1);
        var busy = await _db.AddTypeAsync("BUSY", priority: 2);
        await _db.AddMessageAsync(busy, DateTime.UtcNow.AddHours(-1), end: null);

        Assert.Equal(MessageTypeDeleteOutcome.Busy, await Service().DeleteAsync(busy, default));
        Assert.Equal(MessageTypeDeleteOutcome.Deleted, await Service().DeleteAsync(a, default));

        var deleted = _events.Of(UserEventActionKeys.MessageTypeDeleted);
        Assert.Equal([(UserEventOutcome.Refused, busy.ToString()), (UserEventOutcome.Succeeded, a.ToString())],
            deleted.Select(e => (e.Outcome, e.Target!.Id)));
        Assert.Equal("""{"isDeleted":{"old":false,"new":true}}""", RecordingEventLog.DetailsJson(deleted[1]));
        Assert.Equal("""{"key":"message-types.order","old":["A","BUSY"],"new":["BUSY"],"unit":"order"}""",
            RecordingEventLog.DetailsJson(_events.Single(UserEventActionKeys.BusinessRuleChanged)));
    }
}
