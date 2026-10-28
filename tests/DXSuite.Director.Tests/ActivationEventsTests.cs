using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-53: the <c>activation.*</c> User events of AUD-08 with the Activation as target: a send from the dialog, its
/// priority override, a cancel, and the end the System logs once. Runs against a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ActivationEventsTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    private readonly MessageAccessDatabase _db;

    public ActivationEventsTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task A_dialog_send_logs_activation_sent_as_the_sender_with_the_AUD_08_details()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");
        var events = new RecordingEventLog(Ana);

        var result = await Sending(Ana, events).SendAsync(Canned(message, minutes: 15), null, default);

        var (actor, sent) = Assert.Single(events.Written);
        Assert.Equal((UserEventActorKind.Account, Ana), (actor.Kind, actor.UserId));
        Assert.Equal(UserEventActionKeys.ActivationSent, sent.ActionKey);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Activation, result.MessageActivationId!.Value, "Boarding moved"), sent.Target);
        Assert.Equal(
            $$"""{"source":"DirectorAdmin","oneOff":false,"messageId":{{message}},"mode":"zones","zoneIds":[{{GatesA}}],"displayCount":2,"startUtc":"2026-10-20T14:00:00Z","endUtc":"2026-10-20T14:15:00Z","weeklyWindow":null,"priorityOverride":null}""",
            RecordingEventLog.DetailsJson(sent));
    }

    // AUD-07, AUD-08: a one-off send creates a Message, so it logs message.created as well as activation.sent.
    [LocalDbFact]
    public async Task A_one_off_send_logs_message_created_for_its_new_Message_then_activation_sent()
    {
        await _db.SetLanguagesAsync(Paging, "eng", "fra");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: true, userId: Ana);
        var events = new RecordingEventLog(Ana);

        var result = await Sending(Ana, events).SendAsync(new MessageSendRequest
        {
            MessageTypeId = Paging, OneOffTexts = new Dictionary<string, string> { ["eng"] = "Gate closing", ["fra"] = "Fermeture de la porte" },
            Target = MessageSendTarget.DisplayZones(GatesA), When = new MessageSendTiming.SendNow(15),
        }, null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        Assert.Equal([UserEventActionKeys.MessageCreated, UserEventActionKeys.ActivationSent], events.Events.Select(e => e.ActionKey));
        await using var db = _db.Director();
        var messageId = await db.TblMessages.Where(m => !m.IsCanned).Select(m => m.MessageId).SingleAsync();
        var created = events.Single(UserEventActionKeys.MessageCreated);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Message, messageId.ToString(), "Gate closing"), created.Target);
        Assert.Equal("""{"messageTypeCode":"VISUAL_PAGING","isCanned":false,"status":"Approved","texts":{"eng":"Gate closing","fra":"Fermeture de la porte"}}""",
            RecordingEventLog.DetailsJson(created));
        Assert.Equal(messageId, RecordingEventLog.Details(events.Single(UserEventActionKeys.ActivationSent)).GetProperty("messageId").GetInt32());
    }

    [LocalDbFact]
    public async Task A_send_with_a_priority_override_also_logs_activation_priority_override_set_old_and_new()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");
        var events = new RecordingEventLog(Ana);

        var result = await Sending(Ana, events, PermissionKeys.PageMessages, PermissionKeys.MessagesPriorityOverride)
            .SendAsync(Canned(message) with { PriorityOverride = -5 }, null, default);

        var set = events.Single(UserEventActionKeys.ActivationPriorityOverrideSet);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Activation, result.MessageActivationId!.Value, "Boarding moved"), set.Target);
        Assert.Equal("""{"priorityOverride":{"old":null,"new":-5}}""", RecordingEventLog.DetailsJson(set));
        Assert.Equal(-5, RecordingEventLog.Details(events.Single(UserEventActionKeys.ActivationSent)).GetProperty("priorityOverride").GetInt32());
    }

    [LocalDbFact]
    public async Task A_send_the_senders_rules_do_not_cover_is_refused_and_logged_as_refused()
    {
        var message = await _db.AddMessageAsync(Paging);
        var events = new RecordingEventLog(Ana);

        var result = await Sending(Ana, events).SendAsync(Canned(message), null, default);

        Assert.Equal(MessageSendOutcome.NotAllowed, result.Outcome);
        var refused = events.Single(UserEventActionKeys.ActivationSent);
        Assert.Equal(UserEventOutcome.Refused, refused.Outcome);
        Assert.Null(refused.Target!.Id);
        Assert.Equal(message, RecordingEventLog.Details(refused).GetProperty("messageId").GetInt32());
    }

    [LocalDbFact]
    public async Task A_dialog_send_is_kept_when_its_event_cannot_be_written()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Ana, new RecordingEventLog(Ana) { Fail = true }).SendAsync(Canned(message), null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        await using var db = _db.Director();
        Assert.True(await db.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task Deactivate_logs_activation_cancelled_for_each_Activation_it_cancels()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");
        var running = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var ended = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddHours(-1), [GateA1], GatesA);
        var events = new RecordingEventLog(Bo);

        await Cancelling(Bo, events).CancelAsync([running, ended], default);

        var cancelled = events.Single(UserEventActionKeys.ActivationCancelled);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Activation, running, "Boarding moved"), cancelled.Target);
        Assert.Equal("{}", RecordingEventLog.DetailsJson(cancelled));
        Assert.Equal(Bo, Assert.Single(events.Written).Actor.UserId);
    }

    [LocalDbFact]
    public async Task Deactivate_without_page_messages_is_refused_and_logged_as_refused()
    {
        var message = await _db.AddMessageAsync(Paging);
        var running = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var events = new RecordingEventLog(Ana);

        await Cancelling(Ana, events, PermissionKeys.MessagesAuthor).CancelAsync([running], default);

        var refused = events.Single(UserEventActionKeys.ActivationCancelled);
        Assert.Equal((UserEventOutcome.Refused, running.ToString()), (refused.Outcome, refused.Target!.Id));
    }

    [LocalDbFact]
    public async Task The_end_of_an_Activation_is_logged_once_by_the_System_and_never_for_a_cancelled_or_open_ended_one()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");
        var ended = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddMinutes(-1), [GateA1], GatesA);
        var endsLater = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddMinutes(5), [GateA1], GatesA);
        await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddMinutes(-1), [GateA1], GatesA, cancelledUtc: Now.AddMinutes(-30));
        await _db.AddActivationAsync(message, Now.AddHours(-2), null, [GateA1], GatesA);
        var events = new RecordingEventLog();
        var job = Ended(events);

        Assert.Equal(1, await job.LogEndedAsync(Now, default));
        Assert.Equal(0, await job.LogEndedAsync(Now, default));
        Assert.Equal(1, await job.LogEndedAsync(Now.AddMinutes(6), default));

        Assert.Equal([ended, endsLater], events.Of(UserEventActionKeys.ActivationEnded).Select(e => int.Parse(e.Target!.Id!)));
        var (actor, first) = events.Written.First();
        Assert.Equal((UserEventActorKind.System, ActivationEndedLog.ServiceName), (actor.Kind, actor.ActorName));
        Assert.Equal(("Boarding moved", "{}"), (first.Target!.Name, RecordingEventLog.DetailsJson(first)));
    }

    [LocalDbFact]
    public async Task An_end_whose_event_could_not_be_written_is_logged_on_the_next_pass()
    {
        var message = await _db.AddMessageAsync(Paging);
        var ended = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddMinutes(-1), [GateA1], GatesA);
        var events = new RecordingEventLog { Fail = true };

        Assert.Equal(0, await Ended(events).LogEndedAsync(Now, default));
        events.Fail = false;
        Assert.Equal(1, await Ended(events).LogEndedAsync(Now, default));

        Assert.Equal(ended.ToString(), events.Single(UserEventActionKeys.ActivationEnded).Target!.Id);
    }

    private ActivationEndedLog Ended(RecordingEventLog events) =>
        new(_db.Factory, events, NullLogger<ActivationEndedLog>.Instance);

    private ActiveMessages Cancelling(int userId, RecordingEventLog events, params string[] keys) =>
        new(_db.Factory, Session(userId, keys), new FixedClock(Now),
            new DisplayTimeZones(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }), NullLogger<DisplayTimeZones>.Instance),
            events, NullLogger<ActiveMessages>.Instance);

    private MessageSending Sending(int userId, RecordingEventLog events, params string[] keys)
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions { Enabled = false });
        return new MessageSending(_db.Factory, new MessageSendAccess(_db.Factory), new MessageLanguageCatalogue(d02), Session(userId, keys), d02,
            new FixedClock(Now), new DisplayTimeZones(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }),
                NullLogger<DisplayTimeZones>.Instance), NoAssetVersions(), events, NullLogger<MessageSending>.Instance);
    }

    private static KeySession Session(int userId, string[] keys) =>
        new(keys.Length == 0 ? [PermissionKeys.PageMessages] : keys) { User = new TblUser { UserId = userId, RoleId = userId == Admin ? 1 : 3 } };

    private static MessageSendRequest Canned(int messageId, int? minutes = null) => new()
    {
        MessageTypeId = Paging, MessageId = messageId, Target = MessageSendTarget.DisplayZones(GatesA), When = new MessageSendTiming.SendNow(minutes),
    };

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
