using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using Microsoft.EntityFrameworkCore;
using static DXSuite.Director.Tests.MessageApprovalWorld;

namespace DXSuite.Director.Tests;

// Clause and section numbers refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Approval emails for every chain (B-34, section 12 point 7, A-27): with a chain's email switch on, each submission
/// emails the current stage's approvers, an advance emails the next stage, and approvers get one reminder digest per
/// interval until it is decided. Mail goes to a recording server; time moves on a manual clock.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ApprovalEmailsTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private const string IvyMail = "ivy@gtaa.test", JonMail = "jon@gtaa.test", KimMail = "kim@gtaa.test";

    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _authoring;
    private readonly MessageApprovalWorld _world;

    public ApprovalEmailsTests(MessageTypeDatabase db)
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
    public async Task A_submission_emails_each_approver_of_the_first_stage_once_and_an_advance_emails_the_next_stage()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var duty = await _world.GroupAsync("Duty", Ivy, Kim, Disabled);
        var comms = await _world.GroupAsync("Comms", Jon);
        await _world.ChainAsync(type, email: true, stages: [("OR", [duty]), ("OR", [comms])]);
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));

        await _world.EditAsync(message, "Gate changed to A12");

        Assert.Equal(["Waiting for your approval: An edit to the PAGING name Message \"Gate change\""], _world.Mail.SubjectsTo(IvyMail));
        Assert.Single(_world.Mail.SubjectsTo(KimMail));
        Assert.Empty(_world.Mail.SubjectsTo(JonMail));
        Assert.Equal(2, _world.Mail.Sent.Count);

        _world.Mail.Clear();
        var item = await _world.OnlyQueueItemAsync(Ivy);
        await _world.Approvals(Ivy).ApproveAsync(item.QueueEntryId, null, default);

        Assert.Single(_world.Mail.Sent);
        Assert.Single(_world.Mail.SubjectsTo(JonMail));
        Assert.Contains("stage 2 of 2", _world.Mail.Sent[0].TextBody);
    }

    [LocalDbFact]
    public async Task With_the_switch_off_or_no_SMTP_nothing_is_emailed_and_approval_still_works()
    {
        var quiet = await _db.AddTypeAsync("PAGING", priority: 1);
        var noSmtp = await _db.AddTypeAsync("DELAY", priority: 2);
        var duty = await _world.GroupAsync("Duty", Ivy);
        await _world.ChainAsync(quiet, email: false, stages: ("OR", [duty]));
        await _world.ChainAsync(noSmtp, email: true, stages: ("OR", [duty]));
        await _world.EditAsync(await _world.ApprovedMessageAsync(quiet, "Quiet", ("eng", "Quiet")), "Quiet edit");
        Assert.Empty(_world.Mail.Sent);

        _world.Mail.Configured = false;
        await _world.EditAsync(await _world.ApprovedMessageAsync(noSmtp, "Loud", ("eng", "Loud")), "Loud edit");
        _world.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(0, await _world.Emails().SendDueRemindersAsync(_world.Clock.GetUtcNow().UtcDateTime));

        var queue = await _world.Approvals(Ivy).MyQueueAsync(default);
        Assert.Equal(2, queue.Count);
        foreach (var item in queue)
            Assert.Equal(Services.Messaging.MessageDecisionOutcome.Approved, (await _world.Approvals(Ivy).ApproveAsync(item.QueueEntryId, null, default)).Outcome);
        Assert.Empty(_world.Mail.Sent);
    }

    [LocalDbFact]
    public async Task Each_approver_gets_one_digest_per_interval_of_what_still_waits_and_none_once_it_is_decided()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var duty = await _world.GroupAsync("Duty", Ivy, Kim);
        await _world.ChainAsync(type, email: true, reminderHours: 2, stages: ("OR", [duty]));
        await _world.EditAsync(await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed")), "Gate changed to A12");
        await _world.EditAsync(await _world.ApprovedMessageAsync(type, "Delay", ("eng", "Delayed")), "Delayed 20 minutes");
        _world.Mail.Clear();
        var emails = _world.Emails();

        _world.Clock.Advance(TimeSpan.FromMinutes(119));
        Assert.Equal(0, await emails.SendDueRemindersAsync(Now()));

        _world.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, await emails.SendDueRemindersAsync(Now()));
        Assert.Equal([ApprovalEmails.DigestSubject], _world.Mail.SubjectsTo(IvyMail));
        var digest = _world.Mail.Sent.Single(m => m.To.Contains(IvyMail));
        Assert.Contains("Gate change", digest.TextBody);
        Assert.Contains("Delay", digest.TextBody);
        Assert.Equal(0, await emails.SendDueRemindersAsync(Now()));

        // Kim decides one of them; the next digest lists only what still waits.
        var gateChange = (await _world.Approvals(Kim).MyQueueAsync(default)).Single(i => i.Approved!.Name == "Gate change");
        await _world.Approvals(Kim).ApproveAsync(gateChange.QueueEntryId, null, default);
        _world.Mail.Clear();
        _world.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(2, await emails.SendDueRemindersAsync(Now()));
        var next = _world.Mail.Sent.Single(m => m.To.Contains(IvyMail));
        Assert.DoesNotContain("Gate change", next.TextBody);
        Assert.Contains("Delay", next.TextBody);
    }

    [LocalDbFact]
    public async Task A_resubmission_starts_the_reminder_interval_again()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, email: true, reminderHours: 2, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "First edit");
        _world.Clock.Advance(TimeSpan.FromMinutes(90));

        await _world.EditAsync(message, "Second edit");
        _world.Clock.Advance(TimeSpan.FromMinutes(60));

        Assert.Equal(0, await _world.Emails().SendDueRemindersAsync(Now()));
        _world.Clock.Advance(TimeSpan.FromMinutes(60));
        Assert.Equal(1, await _world.Emails().SendDueRemindersAsync(Now()));
    }

    [LocalDbFact]
    public async Task An_upload_to_a_location_chain_emails_its_approvers_too()
    {
        var duty = await _world.GroupAsync("Duty", Ivy);
        var (location, approvalId) = await _world.UploadToLocationChainAsync("Lobby", duty, email: true);

        await using (var db = _db.Director())
            await _world.ChainService(db).SubmitToChainAsync(approvalId, location, uploadingUserId: AuthoringWorld.UserId);

        Assert.Equal(["Waiting for your approval: \"lobby.png\" for Lobby library"], _world.Mail.SubjectsTo(IvyMail));
    }

    private DateTime Now() => _world.Clock.GetUtcNow().UtcDateTime;
}
