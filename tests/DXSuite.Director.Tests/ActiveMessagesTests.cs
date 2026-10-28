using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause and gap ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The Active Messages tab's list and Deactivate (B-36: MSG-18, ZONE-16, G-13, G-38) over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ActiveMessagesTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    private readonly MessageAccessDatabase _db;

    public ActiveMessagesTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task Deactivate_cancels_the_Activation_as_the_signed_in_user_and_keeps_the_Message()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1, GateA2], GatesA, byUserId: Ana);

        var result = await Service(Bo).CancelAsync([activation], default);

        Assert.Equal(ActivationCancelOutcome.Cancelled, result.Outcome);
        Assert.Equal([activation], result.CancelledIds);
        await using var db = _db.Director();
        var row = await db.TblMessageActivations.Include(a => a.Message).SingleAsync();
        Assert.Equal(Now, row.CancelledUtc);
        Assert.Equal(Bo, row.CancelledByUserId);
        Assert.False(row.Message.IsDeleted);
        Assert.Equal(2, await db.TblMessageActivationTargets.CountAsync());
    }

    // INK-09: a cancel takes the Message off screens, so it moves the delivery version; cancelling nothing does not.
    [LocalDbFact]
    public async Task A_cancel_moves_the_delivery_version_and_one_that_finds_nothing_running_does_not()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var ended = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddHours(-1), [GateA1], GatesA);
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        Assert.Equal(ActivationCancelOutcome.AlreadyOver, (await Service(Bo).CancelAsync([ended], default)).Outcome);
        Assert.Equal(before, await DeliveryVersionProbe.ReadAsync(_db.Director));

        Assert.Equal(ActivationCancelOutcome.Cancelled, (await Service(Bo).CancelAsync([activation], default)).Outcome);
        Assert.NotEqual(before, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    [LocalDbFact]
    public async Task An_Activation_already_over_is_left_as_it_is()
    {
        var message = await _db.AddMessageAsync(Paging);
        var ended = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddHours(-1), [GateA1], GatesA);
        var cancelled = await _db.AddActivationAsync(message, Now.AddHours(-2), null, [GateA1], GatesA,
            cancelledUtc: Now.AddMinutes(-30));
        await using (var db = _db.Director())
            await db.TblMessageActivations.Where(a => a.MessageActivationId == cancelled)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CancelledByUserId, Ana));

        var result = await Service(Bo).CancelAsync([ended, cancelled], default);

        Assert.Equal(ActivationCancelOutcome.AlreadyOver, result.Outcome);
        await using var check = _db.Director();
        var rows = await check.TblMessageActivations.OrderBy(a => a.MessageActivationId).ToListAsync();
        Assert.Null(rows[0].CancelledUtc);
        Assert.Equal((Now.AddMinutes(-30), (int?)Ana), (rows[1].CancelledUtc!.Value, rows[1].CancelledByUserId));
    }

    [LocalDbFact]
    public async Task The_tab_lists_Activations_active_or_scheduled_newest_first_with_their_Target()
    {
        var canned = await _db.AddMessageAsync(Paging, name: "Gate change");
        var oneOff = await _db.AddOneOffAsync(Paging, "Boarding now");
        var active = await _db.AddActivationAsync(canned, Now.AddMinutes(-10), null, [GateA1, GateA2], GatesA, byUserId: Ana);
        var scheduled = await _db.AddActivationAsync(oneOff, Now.AddHours(1), Now.AddHours(2), [GateA2], byUserId: Bo,
            sentUtc: Now.AddMinutes(-5));
        await _db.AddActivationAsync(canned, Now.AddHours(-3), Now.AddHours(-2), [GateA1], GatesA);
        await _db.AddActivationAsync(canned, Now.AddHours(-1), null, [GateA1], GatesA, cancelledUtc: Now.AddMinutes(-1));

        var rows = await Service(Bo).ListAsync(includeOver: false, default);

        Assert.Equal([scheduled, active], rows.Select(r => r.MessageActivationId));
        var first = rows[0];
        Assert.Equal(("Boarding now", true, "Visual Paging", "Gate A2", 1), (first.MessageName, first.IsOneOff, first.TypeName, first.Target, first.ScreenCount));
        Assert.Equal((Now.AddHours(1), (DateTime?)Now.AddHours(2), ActivationStatus.Scheduled, "Bo Duty"),
            (first.StartUtc, first.EndUtc, first.Status, first.SentBy));
        var second = rows[1];
        Assert.Equal(("Gate change", false, "Gates A", 2, (DateTime?)null, ActivationStatus.Active, "Ana Agent"),
            (second.MessageName, second.IsOneOff, second.Target, second.ScreenCount, second.EndUtc, second.Status, second.SentBy));
    }

    [LocalDbFact]
    public async Task A_scheduled_send_is_Active_only_inside_its_weekly_window_on_its_screens_clock()
    {
        // Now is Tuesday 20 Oct 2026, 10:00 in Toronto, where Gate A1's device sits.
        await _db.AddDeviceAsync("dev-a1", GateA1, "America/Toronto");
        var message = await _db.AddMessageAsync(Paging);
        var daytime = await _db.AddActivationAsync(message, default, null, [GateA1], GatesA,
            timing: Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        var evening = await _db.AddActivationAsync(message, default, null, [GateA1], GatesA,
            timing: Weekdays(new TimeOnly(18, 0), new TimeOnly(20, 0)));

        var rows = (await Service(Ana).ListAsync(includeOver: false, default)).ToDictionary(r => r.MessageActivationId);

        Assert.Equal(ActivationStatus.Active, rows[daytime].Status);
        Assert.Equal(ActivationStatus.Scheduled, rows[evening].Status);
        Assert.True(rows[evening].CanCancel);
        Assert.Equal((new DateTime(2026, 10, 19, 0, 0, 0), (DateTime?)new DateTime(2026, 11, 2, 0, 0, 0), "Mon–Fri 18:00–20:00"),
            (rows[evening].StartLocal!.Value, rows[evening].EndLocal, rows[evening].Window));
    }

    private static ResolvedTiming Weekdays(TimeOnly from, TimeOnly until) => ActivationTiming.Resolve(
        new MessageSendTiming.Scheduled(new DateTime(2026, 10, 19, 0, 0, 0), new DateTime(2026, 11, 2, 0, 0, 0),
            new MessageWeeklyWindow(new HashSet<DayOfWeek>
                { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, from, until)),
        Now.AddDays(-2), [TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")]);

    [LocalDbFact]
    public async Task A_cancelled_one_off_stays_in_the_history_and_its_Message_is_kept()
    {
        var oneOff = await _db.AddOneOffAsync(Paging, "Boarding now");
        var activation = await _db.AddActivationAsync(oneOff, Now.AddMinutes(-10), null, [GateA2]);
        var ended = await _db.AddActivationAsync(oneOff, Now.AddHours(-3), Now.AddHours(-2), [GateA2], sentUtc: Now.AddHours(-3));
        var service = Service(Ana);

        await service.CancelAsync([activation], default);

        Assert.Empty(await service.ListAsync(includeOver: false, default));
        var history = await service.ListAsync(includeOver: true, default);
        Assert.Equal([(activation, ActivationStatus.Cancelled), (ended, ActivationStatus.Ended)],
            history.Select(r => (r.MessageActivationId, r.Status)));
        await using var db = _db.Director();
        Assert.False((await db.TblMessages.SingleAsync(m => m.MessageId == oneOff)).IsDeleted);
    }

    [LocalDbFact]
    public async Task Deactivate_on_a_Message_cancels_every_one_of_its_running_Activations_together()
    {
        var message = await _db.AddMessageAsync(Paging);
        var other = await _db.AddMessageAsync(Paging, name: "Other");
        var now = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var later = await _db.AddActivationAsync(message, Now.AddHours(1), null, [CheckIn1], CheckIn);
        await _db.AddActivationAsync(message, Now.AddHours(-3), Now.AddHours(-2), [GateA1], GatesA);
        var untouched = await _db.AddActivationAsync(other, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var service = Service(Ana);

        var running = await service.ListForMessageAsync(message, default);
        Assert.Equal([later, now], running.Select(r => r.MessageActivationId));
        var result = await service.CancelAsync(running.Select(r => r.MessageActivationId).ToList(), default);

        Assert.Equal([now, later], result.CancelledIds.Order());
        Assert.Equal([untouched], (await service.ListAsync(includeOver: false, default)).Select(r => r.MessageActivationId));
    }

    [LocalDbFact]
    public async Task Without_page_messages_nothing_is_listed_or_cancelled()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var service = Service(Ana, PermissionKeys.MessagesAuthor);

        Assert.Empty(await service.ListAsync(includeOver: true, default));
        Assert.Equal(ActivationCancelOutcome.NotAllowed, (await service.CancelAsync([activation], default)).Outcome);
        await using var db = _db.Director();
        Assert.Null((await db.TblMessageActivations.SingleAsync()).CancelledUtc);
    }

    private ActiveMessages Service(int userId, params string[] keys)
    {
        var session = new KeySession(keys.Length == 0 ? [PermissionKeys.PageMessages] : keys)
        {
            User = new TblUser { UserId = userId, RoleId = userId == Admin ? 1 : 3 }
        };
        return new ActiveMessages(_db.Factory, session, new FixedClock(Now), TimeZones(), new RecordingEventLog(userId),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ActiveMessages>.Instance);
    }

    private static DisplayTimeZones TimeZones() =>
        new(Microsoft.Extensions.Options.Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DisplayTimeZones>.Instance);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
