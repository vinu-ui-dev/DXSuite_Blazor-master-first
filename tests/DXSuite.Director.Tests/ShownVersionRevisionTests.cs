using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.CustomerDeviations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageApprovalWorld;

namespace DXSuite.Director.Tests;

// Clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Shown versions of an approved revision (B-80, AUD-06, MSG-13, MSG-15): approving a revision of a canned Message adds
/// one shown version to every Activation of it that is active or scheduled, in the approval's transaction, and starts no
/// Activation. Driven through <see cref="MessageAuthoring"/> and <see cref="MessageApprovals"/> on LocalDB. The first
/// shown version of a send is covered in <see cref="MessageSendingTests"/> and <see cref="MessagingApiTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ShownVersionRevisionTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _authoring;
    private readonly MessageApprovalWorld _world;

    public ShownVersionRevisionTests(MessageTypeDatabase db)
    {
        _db = db;
        _authoring = new AuthoringWorld(db);
        _world = new MessageApprovalWorld(db, _authoring);
    }

    private DateTime Now => _world.Clock.GetUtcNow().UtcDateTime;

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

    // MSG-13 "live" is read as active or scheduled, so a scheduled Activation that has not started also gains the
    // version and never publishes the superseded text.
    [LocalDbFact]
    public async Task An_approved_revision_adds_version_2_to_each_live_Activation_and_starts_no_Activation()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 4, top: false, deleted: false, "eng", "fra");
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Porte changée"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null);
        var scheduled = await _world.ActivationAsync(message, Now.AddDays(1), Now.AddDays(2), priorityOverride: 9);
        var ended = await _world.ActivationAsync(message, Now.AddDays(-2), Now.AddDays(-1));
        var cancelled = await _world.ActivationAsync(message, Now.AddHours(-1), null, cancelled: Now.AddMinutes(-30));
        await _world.EditAsync(message, "Gate changed to A12");
        _world.Clock.Advance(TimeSpan.FromMinutes(10));
        var approvedAt = Now;

        var decision = await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default);

        Assert.Equal(MessageDecisionOutcome.Approved, decision.Outcome);
        foreach (var (activation, rank) in new[] { (showing, 4), (scheduled, 9) })
        {
            var versions = await _world.VersionsAsync(activation);
            Assert.Equal([1, 2], versions.Select(v => v.Sequence));
            var revision = versions[1];
            Assert.Equal((ShownVersionReason.Revision, (int?)1), (revision.Reason, revision.RevisionNumber));
            Assert.Equal((type, "PAGING", rank), (revision.MessageTypeId, revision.MessageTypeCode, revision.EffectiveRank));
            Assert.Equal("""{"eng":"Gate changed to A12","fra":"Porte changée"}""", revision.Texts);
            Assert.Equal("[]", revision.AssetSlots);
            Assert.Equal((approvedAt, (DateTime?)null), (revision.SentUtc, revision.FirstServedUtc));
        }
        Assert.Single(await _world.VersionsAsync(ended));
        Assert.Single(await _world.VersionsAsync(cancelled));
        await using var db = _db.Director();
        Assert.Equal(4, await db.TblMessageActivations.CountAsync());
    }

    // MSG-13, AUD-06: screens keep the approved text until the chain approves the revision.
    [LocalDbFact]
    public async Task A_revision_waiting_on_a_later_stage_and_then_rejected_adds_no_shown_version()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: [("OR", [await _world.GroupAsync("Duty", Ivy)]), ("OR", [await _world.GroupAsync("Leads", Jon)])]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null);
        await _world.EditAsync(message, "Gate changed to A12");

        Assert.Equal(MessageDecisionOutcome.Advanced,
            (await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default)).Outcome);
        Assert.Single(await _world.VersionsAsync(showing));

        Assert.Equal(MessageDecisionOutcome.Rejected,
            (await _world.Approvals(Jon).RejectAsync((await _world.OnlyQueueItemAsync(Jon)).QueueEntryId, "Wrong gate", default)).Outcome);
        Assert.Single(await _world.VersionsAsync(showing));
    }

    // AUD-06: the asset is recorded with the hash of its file as it is when the version is written.
    [LocalDbFact]
    public async Task An_approved_revision_records_each_slots_asset_with_the_hash_of_its_file()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _db.AddSlotAsync(type, "ASL", await _db.AddLibraryConstraintAsync("ASL videos"), required: false);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null);
        var authoring = _world.Authors();
        var draft = (await authoring.OpenAsync(message, default))!;
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_authoring.UploadedFile("sign.mp4", "new video bytes"), "sign.mp4"));
        await authoring.SaveAsync(draft, default);

        await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default);

        await using var db = _db.Director();
        var assetId = await db.TblMessageAssets.Select(a => a.AssetId).SingleAsync();
        // SHA-256 of "new video bytes".
        Assert.Equal($$"""[{"slotKey":"ASL","assetId":{{assetId}},"fileName":"sign.mp4","contentHash":"d76b1cc14ff3aabaae117f9baa14aad8d3777ae9f93c130f51f972dc7182bf8d"}]""",
            (await _world.VersionsAsync(showing))[1].AssetSlots);
    }

    // MSG-11: with no chain on the type an edit is approved when saved, so it is an approved revision like any other.
    [LocalDbFact]
    public async Task With_no_chain_an_edit_to_an_Approved_Message_adds_the_next_shown_version_when_saved()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 2);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null);

        var saved = await _world.EditAsync(message, "Gate changed to A12");

        Assert.Equal((MessageSaveOutcome.Saved, MessageStatus.Approved), (saved.Outcome, saved.Status));
        var revision = (await _world.VersionsAsync(showing))[1];
        Assert.Equal((2, ShownVersionReason.Revision, (int?)1, 2), (revision.Sequence, revision.Reason, revision.RevisionNumber, revision.EffectiveRank));
        Assert.Equal("""{"eng":"Gate changed to A12"}""", revision.Texts);
        Assert.Equal(Now, revision.SentUtc);
        await using var db = _db.Director();
        Assert.Equal(1, (await db.TblMessages.SingleAsync()).RevisionNumber);
    }

    // MSG-13, AUD-06, INK-01: with no chain, a save that leaves what screens show as it was (the same texts and assets,
    // or only a new name) is no revision: no shown version, no RevisionNumber and no delivery version change.
    [LocalDbFact]
    public async Task With_no_chain_a_save_that_changes_nothing_screens_show_adds_no_shown_version_and_moves_nothing()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 2);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null);
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        Assert.Equal(MessageSaveOutcome.Saved, (await _world.EditAsync(message, "Gate changed")).Outcome);
        Assert.Equal(MessageSaveOutcome.Saved, (await _world.EditAsync(message, "Gate changed", name: "Gate change B")).Outcome);

        Assert.Single(await _world.VersionsAsync(showing));
        Assert.Equal(before, await DeliveryVersionProbe.ReadAsync(_db.Director));
        await using var db = _db.Director();
        var saved = await db.TblMessages.SingleAsync();
        Assert.Equal((0, "Gate change B"), (saved.RevisionNumber, saved.Name));
    }

    // MSG-13, INK-07: an edit of an Approved Message under a chain, saved by its author and approved stage by stage,
    // leaves the delivery document exactly as it was until the last stage approves it; then the document carries the
    // new text under the Activation's new shown version.
    [LocalDbFact]
    public async Task An_edit_under_a_chain_reaches_the_delivery_route_only_when_the_last_stage_approves_it()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: [("OR", [await _world.GroupAsync("Duty", Ivy)]), ("OR", [await _world.GroupAsync("Leads", Jon)])]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null, texts: """{"eng":"Gate changed"}""");
        var display = await _world.ShowOnNewDisplayAsync(showing);
        var sent = await DeliveryAsync();
        var first = Assert.Single(Overlay(sent, display));
        Assert.Equal("Gate changed", first.Texts["eng"]);

        Assert.Equal(MessageSaveOutcome.Saved, (await _world.EditAsync(message, "Gate changed to A12")).Outcome);
        Assert.Equal(sent.ETag, (await DeliveryAsync()).ETag);
        Assert.Equal(MessageDecisionOutcome.Advanced,
            (await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default)).Outcome);
        Assert.Equal(sent.ETag, (await DeliveryAsync()).ETag);

        Assert.Equal(MessageDecisionOutcome.Approved,
            (await _world.Approvals(Jon).ApproveAsync((await _world.OnlyQueueItemAsync(Jon)).QueueEntryId, null, default)).Outcome);

        var approved = Assert.Single(Overlay(await DeliveryAsync(), display));
        Assert.Equal("Gate changed to A12", approved.Texts["eng"]);
        Assert.Equal((await _world.VersionsAsync(showing))[1].MessageActivationVersionId, approved.ShownVersionId);
        Assert.NotEqual(first.ShownVersionId, approved.ShownVersionId);
    }

    // INK-09: the approval that changes what screens show moves the delivery version with it; a stage that only
    // advances, and a rejection, leave screens as they are.
    [LocalDbFact]
    public async Task Approving_a_revision_moves_the_delivery_version_and_advancing_or_rejecting_one_does_not()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: [("OR", [await _world.GroupAsync("Duty", Ivy)]), ("OR", [await _world.GroupAsync("Leads", Jon)])]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.ActivationAsync(message, Now.AddHours(-1), null);

        await _world.EditAsync(message, "Gate changed to A12");
        var beforeDecisions = await DeliveryVersionProbe.ReadAsync(_db.Director);
        await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default);
        await _world.Approvals(Jon).RejectAsync((await _world.OnlyQueueItemAsync(Jon)).QueueEntryId, "Wrong gate", default);
        Assert.Equal(beforeDecisions, await DeliveryVersionProbe.ReadAsync(_db.Director));

        await _world.EditAsync(message, "Gate changed to A14");
        await _world.Approvals(Ivy).ApproveAsync((await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId, null, default);
        Assert.Equal(MessageDecisionOutcome.Approved,
            (await _world.Approvals(Jon).ApproveAsync((await _world.OnlyQueueItemAsync(Jon)).QueueEntryId, null, default)).Outcome);
        Assert.NotEqual(beforeDecisions, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    // INK-09: with no chain the save is the approval, so it moves the delivery version itself.
    [LocalDbFact]
    public async Task With_no_chain_an_edit_to_an_Approved_Message_moves_the_delivery_version_when_saved()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 2);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.ActivationAsync(message, Now.AddHours(-1), null);
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        await _world.EditAsync(message, "Gate changed to A12");

        Assert.NotEqual(before, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    [LocalDbFact]
    public async Task A_Message_Type_rank_reorder_adds_no_shown_version()
    {
        var paging = await _db.AddTypeAsync("PAGING", priority: 1);
        var emergency = await _db.AddTypeAsync("EMERGENCY", priority: 2);
        var message = await _world.ApprovedMessageAsync(paging, "Gate change", ("eng", "Gate changed"));
        var showing = await _world.ActivationAsync(message, Now.AddHours(-1), null);

        Assert.True(await _db.Service().MoveAsync(emergency, 0, default));

        Assert.Equal([(emergency, 1), (paging, 2)], await _db.LiveRanksAsync());
        Assert.Single(await _world.VersionsAsync(showing));
    }

    private Task<DeliveryDocument> DeliveryAsync() =>
        new MessageDelivery(_db.Factory, new MessageArbitration(Options.Create(new MessagingOptions())),
                new DisplayTimeZones(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }), NullLogger<DisplayTimeZones>.Instance),
                Options.Create(new D02GtaaMessagingOptions()), _world.Clock, NullLogger<MessageDelivery>.Instance)
            .BuildAsync(default);

    private static IReadOnlyList<DeliveryEntry> Overlay(DeliveryDocument document, int displayId) =>
        document.Body.Displays.Single(d => d.DisplayId == displayId).Overlay;
}
