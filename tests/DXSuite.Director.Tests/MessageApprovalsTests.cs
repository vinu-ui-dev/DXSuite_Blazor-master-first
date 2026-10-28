using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using static DXSuite.Director.Tests.MessageApprovalWorld;

namespace DXSuite.Director.Tests;

// Clause and gap ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Pending revisions and their approval (B-34, MSG-11, MSG-13, MSG-15): an edit to an Approved Message waits beside
/// the approved text in its approvers' My Queue, and one decision covers every language and the ASL video. Driven
/// through <see cref="MessageAuthoring"/> and <see cref="MessageApprovals"/> against a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageApprovalsTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _authoring;
    private readonly MessageApprovalWorld _world;

    public MessageApprovalsTests(MessageTypeDatabase db)
    {
        _db = db;
        _authoring = new AuthoringWorld(db);
        _world = new MessageApprovalWorld(db, _authoring);
    }

    public async Task InitializeAsync()
    {
        await _db.ResetAsync();
        if (LocalDbFactAttribute.IsAvailable) await _world.SeedApproversAsync();
    }

    public Task DisposeAsync()
    {
        _authoring.Dispose();
        return Task.CompletedTask;
    }

    [LocalDbFact]
    public async Task Editing_an_Approved_Message_holds_the_edit_as_a_pending_revision_and_keeps_the_approved_text()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Porte changée"));

        var result = await _world.EditAsync(message, "Gate changed to A12", name: "Gate change A12");

        Assert.Equal((MessageSaveOutcome.Saved, MessageStatus.Approved), (result.Outcome, result.Status));
        var item = await _world.OnlyQueueItemAsync(Ivy);
        Assert.Equal(message, item.MessageId);
        Assert.Equal(("Gate change", "Gate changed", "Porte changée"),
            (item.Approved!.Name, item.Approved.Texts["eng"], item.Approved.Texts["fra"]));
        Assert.Equal(("Gate change A12", "Gate changed to A12", "Porte changée"),
            (item.Proposed.Name, item.Proposed.Texts["eng"], item.Proposed.Texts["fra"]));
        // Screens read the Message itself, which still holds the approved text.
        await using var db = _db.Director();
        var saved = await db.TblMessages.Include(m => m.Texts).SingleAsync();
        Assert.Equal(("Gate change", "Gate changed", 0), (saved.Name, saved.Texts.Single(t => t.LanguageCode == "eng").Body, saved.RevisionNumber));
    }

    [LocalDbFact]
    public async Task A_second_edit_replaces_the_pending_revision_and_opens_from_it()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "First edit");

        var draft = (await _world.Authors().OpenAsync(message, default))!;
        Assert.Equal("First edit", draft.Texts[0].Body);
        Assert.True(draft.HasPendingRevision);
        await _world.EditAsync(message, "Second edit");

        var item = await _world.OnlyQueueItemAsync(Ivy);
        Assert.Equal(("Gate changed", "Second edit"), (item.Approved!.Texts["eng"], item.Proposed.Texts["eng"]));
        await using var db = _db.Director();
        Assert.Equal(1, await db.TblMessageRevisions.CountAsync());
    }

    [LocalDbFact]
    public async Task Approving_a_revision_makes_it_the_approved_text_and_starts_no_Activation()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Porte changée"));
        await _world.EditAsync(message, "Gate changed to A12");
        var item = await _world.OnlyQueueItemAsync(Ivy);

        var decision = await _world.Approvals(Ivy).ApproveAsync(item.QueueEntryId, notes: null, default);

        Assert.Equal(MessageDecisionOutcome.Approved, decision.Outcome);
        Assert.Empty(await _world.Approvals(Ivy).MyQueueAsync(default));
        await using var db = _db.Director();
        var saved = await db.TblMessages.Include(m => m.Texts).SingleAsync();
        Assert.Equal(MessageStatus.Approved, saved.Status);
        Assert.Equal(1, saved.RevisionNumber);
        Assert.Equal(
            [("eng", "Gate changed to A12"), ("fra", "Porte changée")],
            saved.Texts.OrderBy(t => t.LanguageCode).Select(t => (t.LanguageCode, t.Body)));
        Assert.False(await db.TblMessageRevisions.AnyAsync());
        Assert.False(await db.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task Rejecting_a_revision_discards_it_and_leaves_the_approved_text()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");
        var item = await _world.OnlyQueueItemAsync(Ivy);

        var decision = await _world.Approvals(Ivy).RejectAsync(item.QueueEntryId, "Wrong gate", default);

        Assert.Equal(MessageDecisionOutcome.Rejected, decision.Outcome);
        await using var db = _db.Director();
        var saved = await db.TblMessages.Include(m => m.Texts).SingleAsync();
        Assert.Equal((MessageStatus.Approved, "Gate changed", 0), (saved.Status, saved.Texts.Single().Body, saved.RevisionNumber));
        Assert.False(await db.TblMessageRevisions.AnyAsync());
        Assert.Equal(("declined", "Wrong gate"), (await db.TblApprovalRequests.Select(r => r.Status).SingleAsync(),
            await db.TblApprovalActions.Select(a => a.Notes).SingleAsync()));
    }

    [LocalDbFact]
    public async Task A_new_Message_is_approved_or_rejected_by_its_chain()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var approved = await NewMessageAsync(type, "Boarding");
        var rejected = await NewMessageAsync(type, "Delay");

        var queue = await _world.Approvals(Ivy).MyQueueAsync(default);
        Assert.Equal(2, queue.Count);
        Assert.All(queue, i => Assert.Null(i.Approved));
        await _world.Approvals(Ivy).ApproveAsync(queue.Single(i => i.MessageId == approved).QueueEntryId, null, default);
        await _world.Approvals(Ivy).RejectAsync(queue.Single(i => i.MessageId == rejected).QueueEntryId, "No", default);

        await using var db = _db.Director();
        var statuses = await db.TblMessages.ToDictionaryAsync(m => m.MessageId, m => m.Status);
        Assert.Equal(MessageStatus.Approved, statuses[approved]);
        // A rejected new Message goes back to its author as a Draft, which cannot be sent; saving it submits it again.
        Assert.Equal(MessageStatus.Draft, statuses[rejected]);
        Assert.Equal(MessageStatus.Pending, (await _world.EditAsync(rejected, "Delay of 20 minutes")).Status);
        Assert.Single(await _world.Approvals(Ivy).MyQueueAsync(default));
    }

    [LocalDbFact]
    public async Task Only_members_of_the_current_stages_groups_see_and_decide_it_and_approval_moves_through_every_stage()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var duty = await _world.GroupAsync("Duty", Ivy);
        var comms = await _world.GroupAsync("Comms", Jon, Disabled);
        await _world.ChainAsync(type, stages: [("OR", [duty]), ("OR", [comms])]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");

        Assert.Empty(await _world.Approvals(Jon).MyQueueAsync(default));
        var first = await _world.OnlyQueueItemAsync(Ivy);
        Assert.Equal((1, 2, "Duty"), (first.StageNumber, first.StageCount, first.GroupName));
        Assert.Equal(MessageDecisionOutcome.NotAllowed, (await _world.Approvals(Jon).ApproveAsync(first.QueueEntryId, null, default)).Outcome);

        Assert.Equal(MessageDecisionOutcome.Advanced, (await _world.Approvals(Ivy).ApproveAsync(first.QueueEntryId, null, default)).Outcome);
        Assert.Empty(await _world.Approvals(Ivy).MyQueueAsync(default));
        var second = await _world.OnlyQueueItemAsync(Jon);
        Assert.Equal((2, 2), (second.StageNumber, second.StageCount));
        await using (var db = _db.Director())
            Assert.Equal("Gate changed", (await db.TblMessageTexts.SingleAsync()).Body);

        Assert.Equal(MessageDecisionOutcome.Approved, (await _world.Approvals(Jon).ApproveAsync(second.QueueEntryId, null, default)).Outcome);
        await using (var db = _db.Director())
            Assert.Equal("Gate changed to A12", (await db.TblMessageTexts.SingleAsync()).Body);
    }

    [LocalDbFact]
    public async Task An_AND_stage_waits_for_every_group_and_a_decided_entry_cannot_be_decided_again()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("AND", [await _world.GroupAsync("Duty", Ivy), await _world.GroupAsync("Comms", Jon)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");
        var ivys = await _world.OnlyQueueItemAsync(Ivy);
        var jons = await _world.OnlyQueueItemAsync(Jon);

        Assert.Equal(MessageDecisionOutcome.GroupRecorded, (await _world.Approvals(Ivy).ApproveAsync(ivys.QueueEntryId, null, default)).Outcome);
        Assert.Equal(MessageDecisionOutcome.AlreadyDecided, (await _world.Approvals(Ivy).RejectAsync(ivys.QueueEntryId, null, default)).Outcome);
        Assert.Equal(MessageDecisionOutcome.Approved, (await _world.Approvals(Jon).ApproveAsync(jons.QueueEntryId, null, default)).Outcome);
    }

    [LocalDbFact]
    public async Task One_decision_covers_every_language_and_the_ASL_video()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        var constraint = await _db.AddLibraryConstraintAsync("ASL videos");
        await _db.AddSlotAsync(type, "ASL", constraint, required: false);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Porte changée"));

        var authoring = _world.Authors();
        var draft = (await authoring.OpenAsync(message, default))!;
        draft.Texts[0].Body = "Gate changed to A12";
        draft.Texts[1].Body = "Porte changée à A12";
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_authoring.UploadedFile("sign.mp4"), "sign.mp4"));
        await authoring.SaveAsync(draft, default);

        var item = await _world.OnlyQueueItemAsync(Ivy);
        Assert.Equal(["eng", "fra"], item.Languages.Select(l => l.Code));
        Assert.Empty(item.Approved!.Assets);
        Assert.Equal(("ASL", "sign.mp4"), (item.Proposed.Assets.Single().SlotKey, item.Proposed.Assets.Single().AssetName));
        await using (var db = _db.Director())
            Assert.False(await db.TblMessageAssets.AnyAsync());

        await _world.Approvals(Ivy).ApproveAsync(item.QueueEntryId, null, default);

        await using var after = _db.Director();
        Assert.Equal("sign.mp4", (await after.TblMessageAssets.Include(a => a.Asset).SingleAsync()).Asset.AssetName);
        Assert.Equal(["Gate changed to A12", "Porte changée à A12"], await after.TblMessageTexts.OrderBy(t => t.LanguageCode).Select(t => t.Body).ToListAsync());
    }

    [LocalDbFact]
    public async Task New_stages_on_the_types_chain_put_waiting_Messages_on_their_first_stage()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var duty = await _world.GroupAsync("Duty", Ivy);
        var comms = await _world.GroupAsync("Comms", Jon);
        await _world.ChainAsync(type, stages: ("OR", [duty]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");

        await SaveChainAsync(type, c => c.Stages = [new() { GroupIds = [comms] }]);

        Assert.Empty(await _world.Approvals(Ivy).MyQueueAsync(default));
        var item = await _world.OnlyQueueItemAsync(Jon);
        Assert.Equal("Gate changed to A12", item.Proposed.Texts["eng"]);
    }

    [LocalDbFact]
    public async Task A_chain_change_that_keeps_its_stages_keeps_the_approvals_already_made()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: [("OR", [await _world.GroupAsync("Duty", Ivy)]), ("OR", [await _world.GroupAsync("Comms", Jon)])]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");
        await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default);

        await SaveChainAsync(type, c => { c.EmailApprovers = true; c.ReminderIntervalHours = 4; });

        Assert.Equal(2, (await _world.OnlyQueueItemAsync(Jon)).StageNumber);
    }

    [LocalDbFact]
    public async Task Switching_the_chain_off_takes_waiting_Messages_off_the_queue_and_their_next_save_approves_them()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, email: true, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");

        await SaveChainAsync(type, c => c.IsEnabled = false);

        Assert.Empty(await _world.Approvals(Ivy).MyQueueAsync(default));
        _world.Clock.Advance(TimeSpan.FromDays(3));
        _world.Mail.Clear();
        Assert.Equal(0, await _world.Emails().SendDueRemindersAsync(_world.Clock.GetUtcNow().UtcDateTime));
        await using (var db = _db.Director())
            Assert.Equal("Gate changed", (await db.TblMessageTexts.SingleAsync()).Body);

        Assert.Equal(MessageStatus.Approved, (await _world.EditAsync(message, "Gate changed to A14")).Status);
        await using var after = _db.Director();
        Assert.Equal("Gate changed to A14", (await after.TblMessageTexts.SingleAsync()).Body);
        Assert.False(await after.TblMessageRevisions.AnyAsync());
    }

    private async Task SaveChainAsync(int type, Action<Services.ApprovalChainDraft> change)
    {
        var chains = _world.Chains();
        var draft = (await chains.OpenAsync(type))!.Chain;
        change(draft);
        Assert.Equal(MessageChainSaveOutcome.Saved, (await chains.SaveAsync(type, draft)).Outcome);
    }

    private async Task<int> NewMessageAsync(int type, string name)
    {
        var authoring = _world.Authors();
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = name;
        draft.Texts[0].Body = name + " text";
        return (await authoring.SaveAsync(draft, default)).MessageId!.Value;
    }
}
