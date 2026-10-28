using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Tests;

// Clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-53: the <c>message.*</c> User events of AUD-08, written by authoring and approval after their change commits,
/// each with the Message as target and the details the table fixes. Runs against a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageEventsTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _authoring;
    private readonly MessageApprovalWorld _world;

    public MessageEventsTests(MessageTypeDatabase db)
    {
        _db = db;
        _authoring = new AuthoringWorld(db);
        _world = new MessageApprovalWorld(db, _authoring);
    }

    public async Task InitializeAsync()
    {
        await _db.ResetAsync();
        await _world.SeedApproversAsync();
    }

    public Task DisposeAsync()
    {
        _authoring.Dispose();
        return Task.CompletedTask;
    }

    private RecordingEventLog Events => _authoring.Events;

    [LocalDbFact]
    public async Task A_new_Message_logs_message_created_with_its_type_status_and_texts()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        var authoring = _authoring.Service();
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Gate change";
        draft.Texts[0].Body = "Gate changed";
        draft.Texts[1].Body = "Nouvelle porte";

        var saved = await authoring.SaveAsync(draft, default);

        var created = Events.Single(UserEventActionKeys.MessageCreated);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, saved.MessageId!.Value, "Gate change"), created.Target);
        Assert.Equal(UserEventOutcome.Succeeded, created.Outcome);
        Assert.Equal("""{"messageTypeCode":"PAGING","isCanned":true,"status":"Approved","texts":{"eng":"Gate changed","fra":"Nouvelle porte"}}""",
            RecordingEventLog.DetailsJson(created));
        Assert.Equal(AuthoringWorld.UserId, Assert.Single(Events.Written).Actor.UserId);
    }

    [LocalDbFact]
    public async Task An_edit_saved_in_place_logs_message_edited_with_only_the_changed_fields_old_and_new()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Nouvelle porte"));

        await _world.EditAsync(message, "Gate changed to A12", name: "Gate change A12");

        var edited = Events.Single(UserEventActionKeys.MessageEdited);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, message, "Gate change A12"), edited.Target);
        Assert.Equal("""{"name":{"old":"Gate change","new":"Gate change A12"},"texts":{"old":{"eng":"Gate changed"},"new":{"eng":"Gate changed to A12"}}}""",
            RecordingEventLog.DetailsJson(edited));
    }

    [LocalDbFact]
    public async Task An_edit_held_for_approval_logs_message_revision_submitted_with_the_revision_and_its_proposed_texts()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", MessageApprovalWorld.Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Nouvelle porte"));

        await _world.EditAsync(message, "Gate changed to A12");

        await using var db = _db.Director();
        var revisionId = await db.TblMessageRevisions.Select(r => r.MessageRevisionId).SingleAsync();
        var submitted = Events.Single(UserEventActionKeys.MessageRevisionSubmitted);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, message, "Gate change"), submitted.Target);
        Assert.Equal($$$"""{"revisionId":{{{revisionId}}},"texts":{"eng":"Gate changed to A12","fra":"Nouvelle porte"}}""",
            RecordingEventLog.DetailsJson(submitted));
        Assert.Empty(Events.Of(UserEventActionKeys.MessageEdited));
    }

    [LocalDbFact]
    public async Task Each_stage_approval_logs_message_approved_with_the_revision_and_the_stage_as_the_approver()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages:
        [
            ("OR", [await _world.GroupAsync("Duty", MessageApprovalWorld.Ivy)]),
            ("OR", [await _world.GroupAsync("Supervisors", MessageApprovalWorld.Kim)]),
        ]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");
        await using (var db = _db.Director())
        {
            var revisionId = await db.TblMessageRevisions.Select(r => r.MessageRevisionId).SingleAsync();
            var ivy = new RecordingEventLog(MessageApprovalWorld.Ivy);
            var kim = new RecordingEventLog(MessageApprovalWorld.Kim);

            await _world.Approvals(MessageApprovalWorld.Ivy, ivy).ApproveAsync((await _world.OnlyQueueItemAsync(MessageApprovalWorld.Ivy)).QueueEntryId, null, default);
            await _world.Approvals(MessageApprovalWorld.Kim, kim).ApproveAsync((await _world.OnlyQueueItemAsync(MessageApprovalWorld.Kim)).QueueEntryId, null, default);

            var first = ivy.Single(UserEventActionKeys.MessageApproved);
            Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, message, "Gate change"), first.Target);
            Assert.Equal($$"""{"revisionId":{{revisionId}},"chainStage":1}""", RecordingEventLog.DetailsJson(first));
            Assert.Equal($$"""{"revisionId":{{revisionId}},"chainStage":2}""", RecordingEventLog.DetailsJson(kim.Single(UserEventActionKeys.MessageApproved)));
            Assert.Equal(MessageApprovalWorld.Kim, Assert.Single(kim.Written).Actor.UserId);
        }
    }

    [LocalDbFact]
    public async Task Rejecting_a_new_Message_logs_message_rejected_with_no_revision_and_the_reason()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", MessageApprovalWorld.Ivy)]));
        var saved = await SaveNewAsync(type, "Evacuate");
        var ivy = new RecordingEventLog(MessageApprovalWorld.Ivy);

        await _world.Approvals(MessageApprovalWorld.Ivy, ivy)
            .RejectAsync((await _world.OnlyQueueItemAsync(MessageApprovalWorld.Ivy)).QueueEntryId, "  Wrong gate ", default);

        var rejected = ivy.Single(UserEventActionKeys.MessageRejected);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, saved, "Evacuate"), rejected.Target);
        Assert.Equal("""{"revisionId":null,"reason":"Wrong gate"}""", RecordingEventLog.DetailsJson(rejected));
    }

    [LocalDbFact]
    public async Task A_decision_by_someone_outside_the_stages_group_is_refused_and_logged_as_refused()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", MessageApprovalWorld.Ivy)]));
        var saved = await SaveNewAsync(type, "Evacuate");
        var entry = (await _world.OnlyQueueItemAsync(MessageApprovalWorld.Ivy)).QueueEntryId;
        var jon = new RecordingEventLog(MessageApprovalWorld.Jon);

        var decision = await _world.Approvals(MessageApprovalWorld.Jon, jon).ApproveAsync(entry, null, default);

        Assert.Equal(MessageDecisionOutcome.NotAllowed, decision.Outcome);
        var refused = jon.Single(UserEventActionKeys.MessageApproved);
        Assert.Equal((UserEventOutcome.Refused, saved.ToString()), (refused.Outcome, refused.Target!.Id));
    }

    [LocalDbFact]
    public async Task A_save_without_the_author_key_is_refused_and_logged_as_refused()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var authoring = _authoring.Service(author: false);
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Evacuate";
        draft.Texts[0].Body = "Leave the building";

        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Forbidden, result.Outcome);
        var refused = Events.Single(UserEventActionKeys.MessageCreated);
        Assert.Equal(UserEventOutcome.Refused, refused.Outcome);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_save_that_fails_logs_its_attempt_as_failed_and_a_failed_log_write_never_fails_a_save()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _db.AddSlotAsync(type, "ASL", await _db.AddLibraryConstraintAsync("ASL video"), required: false);
        var authoring = _authoring.Service();
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Evacuate";
        draft.Texts[0].Body = "Leave the building";
        // An asset that no longer exists: the database refuses the slot row, so the save fails after every check passed.
        draft.Slots[0].AssetId = int.MaxValue;

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => authoring.SaveAsync(draft, default));

        var failed = Events.Single(UserEventActionKeys.MessageCreated);
        Assert.Equal((UserEventOutcome.Failed, "Evacuate"), (failed.Outcome, failed.Target!.Name));

        Events.Fail = true;
        Assert.Equal(MessageSaveOutcome.Saved, (await SaveNewInternalAsync(type, "Evacuate")).Outcome);
    }

    [LocalDbFact]
    public async Task Retiring_and_deleting_log_message_retired_and_message_deleted_with_no_details()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var retired = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        var deleted = await _world.ApprovedMessageAsync(type, "Evacuate", ("eng", "Leave the building"));
        var authoring = _authoring.Service();

        await authoring.RetireAsync(retired, default);
        await authoring.DeleteAsync(deleted, default);

        var retiredEvent = Events.Single(UserEventActionKeys.MessageRetired);
        var deletedEvent = Events.Single(UserEventActionKeys.MessageDeleted);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, retired, "Gate change"), retiredEvent.Target);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, deleted, "Evacuate"), deletedEvent.Target);
        Assert.Equal(("{}", "{}"), (RecordingEventLog.DetailsJson(retiredEvent), RecordingEventLog.DetailsJson(deletedEvent)));
    }

    [LocalDbFact]
    public async Task Deleting_a_Message_that_is_on_screen_is_refused_and_logged_as_refused()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var message = await _db.AddMessageAsync(type, DateTime.UtcNow.AddHours(-1), end: null);

        Assert.Equal(MessageRemovalOutcome.Busy, await _authoring.Service().DeleteAsync(message, default));

        Assert.Equal(UserEventOutcome.Refused, Events.Single(UserEventActionKeys.MessageDeleted).Outcome);
    }

    private async Task<int> SaveNewAsync(int type, string name) => (await SaveNewInternalAsync(type, name)).MessageId!.Value;

    private async Task<MessageSaveResult> SaveNewInternalAsync(int type, string name)
    {
        var authoring = _world.Authors();
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = name;
        draft.Texts[0].Body = "Leave the building";
        return await authoring.SaveAsync(draft, default);
    }
}
