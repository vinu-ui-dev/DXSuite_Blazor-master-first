using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause and gap ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The send (B-35 part 1): who may send what where, Send now, one-off text, and the Activation with its frozen screen
/// rows saved in one go, over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageSendingTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    private readonly MessageAccessDatabase _db;
    private readonly TempMediaRoot _media = new();

    public MessageSendingTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync()
    {
        _media.Dispose();
        return Task.CompletedTask;
    }

    [LocalDbFact]
    public async Task Send_now_to_a_Display_Zone_saves_the_Activation_and_one_frozen_row_per_display()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA), minutes: 15), null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        Assert.Equal(2, result.ScreenCount);
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.Include(a => a.Targets).SingleAsync();
        Assert.Equal(result.MessageActivationId, activation.MessageActivationId);
        Assert.Equal(message, activation.MessageId);
        Assert.Equal(MessageActivationSource.DirectorAdmin, activation.Source);
        Assert.Equal(Ana, activation.ActivatedByUserId);
        Assert.Equal(Now, activation.ActivatedUtc);
        Assert.Equal(Now, activation.StartUtc);
        Assert.Equal(Now.AddMinutes(15), activation.EndUtc);
        Assert.Equal([(GateA1, (int?)GatesA), (GateA2, GatesA)],
            activation.Targets.OrderBy(t => t.DisplayId).Select(t => (t.DisplayId!.Value, t.ZoneId)));
    }

    [LocalDbFact]
    public async Task Until_cancelled_has_no_end_and_display_and_hide_seconds_are_frozen_from_the_type()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default);
        await using (var db = _db.Director())
            await db.TblMessageTypes.Where(t => t.MessageTypeId == Paging)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DefaultDisplaySeconds, 99).SetProperty(t => t.DefaultHideSeconds, 99));

        await using var check = _db.Director();
        var activation = await check.TblMessageActivations.SingleAsync();
        Assert.Null(activation.EndUtc);
        Assert.Equal(10, activation.DisplaySecondsOverride);
        Assert.Equal(30, activation.HideSecondsOverride);
    }

    [LocalDbFact]
    public async Task Display_and_hide_seconds_the_sender_set_are_kept()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)) with { DisplaySeconds = 5, HideSeconds = 0 }, null, default);

        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.SingleAsync();
        Assert.Equal(5, activation.DisplaySecondsOverride);
        Assert.Equal(0, activation.HideSecondsOverride);
    }

    [LocalDbFact]
    public async Task A_display_reached_through_two_Display_Zones_gets_one_row_through_the_first_in_list_order()
    {
        await _db.AddRuleAsync(Paging, null, userId: Ana);
        await using (var db = _db.Director())
        {
            db.TblZoneDisplays.Add(new TblZoneDisplay { ZoneId = CheckIn, DisplayId = GateA1 });
            await db.SaveChangesAsync();
        }
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(CheckIn, GatesA)), null, default);

        Assert.Equal(3, result.ScreenCount);
        await using var check = _db.Director();
        var rows = await check.TblMessageActivationTargets.OrderBy(t => t.DisplayId).Select(t => new { t.DisplayId, t.ZoneId }).ToListAsync();
        Assert.Equal([(GateA1, (int?)GatesA), (GateA2, GatesA), (CheckIn1, CheckIn)], rows.Select(r => (r.DisplayId!.Value, r.ZoneId)));
    }

    // ZONE-12, A-33: a zone whose displays all came through another picked zone has no screen row, so the
    // Activation records every zone picked as well.
    [LocalDbFact]
    public async Task Every_Display_Zone_picked_is_recorded_even_one_whose_displays_all_came_through_another()
    {
        await _db.SetZoneDisplaysAsync(CheckIn, GateA1);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Admin).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA, CheckIn)), null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.Include(a => a.Targets).Include(a => a.ChosenZones).SingleAsync();
        Assert.All(activation.Targets, t => Assert.Equal(GatesA, t.ZoneId));
        Assert.Equal([GatesA, CheckIn], activation.ChosenZones.Select(z => z.ZoneId).Order());
    }

    [LocalDbFact]
    public async Task One_display_mode_saves_one_row_with_no_Display_Zone()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.Display(GateA2)), null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        await using var db = _db.Director();
        var row = await db.TblMessageActivationTargets.SingleAsync();
        Assert.Equal(GateA2, row.DisplayId);
        Assert.Null(row.ZoneId);
    }

    [LocalDbFact]
    public async Task Each_frozen_row_records_the_display_and_location_names_at_send()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default);

        await using var db = _db.Director();
        var rows = await db.TblMessageActivationTargets.OrderBy(t => t.DisplayId).Select(t => new { t.DisplayName, t.LocationName }).ToListAsync();
        Assert.Equal([("Gate A1", "Terminal 1 Gates"), ("Gate A2", "Terminal 1 Gates")], rows.Select(r => (r.DisplayName, r.LocationName)));
    }

    [LocalDbFact]
    public async Task One_display_mode_refuses_a_display_outside_the_Display_Zones_the_rules_cover()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.Display(Lounge)), null, default);

        Assert.Equal(MessageSendOutcome.NotAllowed, result.Outcome);
        Assert.Contains("Lounge", result.Problem);
        await using var db = _db.Director();
        Assert.False(await db.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_rule_for_every_Display_Zone_lets_one_display_mode_reach_a_display_in_no_zone()
    {
        await _db.AddRuleAsync(Paging, null, groupId: Rmu);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Bo).SendAsync(Canned(message, MessageSendTarget.Display(Lounge)), null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
    }

    [LocalDbFact]
    public async Task A_target_naming_Display_Zones_and_a_display_or_no_zone_at_all_is_refused()
    {
        var message = await _db.AddMessageAsync(Paging);
        var sending = Sending(Admin);

        var both = await sending.SendAsync(Canned(message, new MessageSendTarget(MessageTargetMode.DisplayZones, [GatesA], GateA1)), null, default);
        var none = await sending.SendAsync(Canned(message, MessageSendTarget.DisplayZones()), null, default);

        Assert.Equal(MessageSendOutcome.Invalid, both.Outcome);
        Assert.Contains(MessageSending.TargetField, both.Errors.Keys);
        Assert.Equal(MessageSendOutcome.Invalid, none.Outcome);
        await using var db = _db.Director();
        Assert.False(await db.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_retired_Display_Zone_cannot_be_targeted_even_by_Admin()
    {
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Admin).SendAsync(Canned(message, MessageSendTarget.DisplayZones(OldCheckIn)), null, default);

        Assert.Equal(MessageSendOutcome.NotAllowed, result.Outcome);
    }

    [LocalDbFact]
    public async Task The_frozen_screen_list_stays_as_sent_after_a_Display_Zone_edit()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default);

        await using (var db = _db.Director())
        {
            await db.TblZoneDisplays.Where(zd => zd.ZoneId == GatesA && zd.DisplayId == GateA2).ExecuteDeleteAsync();
            db.TblZoneDisplays.Add(new TblZoneDisplay { ZoneId = GatesA, DisplayId = Lounge });
            await db.SaveChangesAsync();
        }

        await using var check = _db.Director();
        Assert.Equal([GateA1, GateA2], await check.TblMessageActivationTargets.OrderBy(t => t.DisplayId).Select(t => t.DisplayId!.Value).ToListAsync());
    }

    [LocalDbFact]
    public async Task The_Confirm_screens_list_is_what_saves_and_a_Display_Zone_edit_in_between_asks_again()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        var sending = Sending(Ana);
        var request = Canned(message, MessageSendTarget.DisplayZones(GatesA));

        var check = await sending.CheckAsync(request, default);
        Assert.Equal(MessageSendOutcome.Ready, check.Outcome);
        Assert.Equal(["Gate A1", "Gate A2"], check.Screens.Select(s => s.DisplayName));
        await using (var db = _db.Director())
        {
            db.TblZoneDisplays.Add(new TblZoneDisplay { ZoneId = GatesA, DisplayId = Lounge });
            await db.SaveChangesAsync();
        }

        var result = await sending.SendAsync(request, check.Screens.Select(s => s.DisplayId).ToList(), default);

        Assert.Equal(MessageSendOutcome.ScreensChanged, result.Outcome);
        Assert.Equal(3, result.ScreenCount);
        await using var after = _db.Director();
        Assert.False(await after.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task Only_an_Approved_canned_Message_can_be_sent()
    {
        var pending = await _db.AddMessageAsync(Paging, MessageStatus.Pending);
        var sending = Sending(Admin);

        var result = await sending.SendAsync(Canned(pending, MessageSendTarget.DisplayZones(GatesA)), null, default);

        Assert.Equal(MessageSendOutcome.NotSendable, result.Outcome);
        Assert.Null(await sending.OpenAsync(pending, default));
        Assert.NotNull(await sending.OpenAsync(await _db.AddMessageAsync(Paging), default));
    }

    [LocalDbFact]
    public async Task A_one_off_is_refused_without_a_rule_that_allows_one_off_text()
    {
        await _db.SetLanguagesAsync(Paging, "eng");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: false, userId: Ana);
        var sending = Sending(Ana);

        var result = await sending.SendAsync(OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", "Gate B4 is now open")), null, default);

        Assert.Equal(MessageSendOutcome.NotAllowed, result.Outcome);
        Assert.Contains("one-off text", result.Problem);
        Assert.False(await sending.CanSendOneOffAsync(Paging, default));
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_one_off_under_a_rule_that_allows_it_saves_an_unapproved_text_with_its_Activation()
    {
        await _db.SetLanguagesAsync(Paging, "eng", "fra");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: true, userId: Ana);
        var sending = Sending(Ana);
        Assert.True(await sending.CanSendOneOffAsync(Paging, default));

        var result = await sending.SendAsync(
            OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", " Gate B4 is now open "), ("fra", "La porte B4 est ouverte")), null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.Include(a => a.Message).ThenInclude(m => m.Texts).Include(a => a.Targets).SingleAsync();
        Assert.False(activation.Message.IsCanned);
        Assert.Equal(MessageStatus.Approved, activation.Message.Status);
        Assert.Equal("Gate B4 is now open", activation.Message.Name);
        Assert.Equal(["Gate B4 is now open", "La porte B4 est ouverte"], activation.Message.Texts.OrderBy(t => t.LanguageCode).Select(t => t.Body));
        Assert.Equal(2, activation.Targets.Count);
    }

    // MSG-04: a Message of a type with a required Asset Slot holds a file before it saves; a one-off holds none.
    [LocalDbFact]
    public async Task A_type_with_a_required_Asset_Slot_offers_and_takes_no_one_off_text()
    {
        await _db.SetLanguagesAsync(Paging, "eng");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: true, userId: Ana);
        await _db.AddSlotAsync(Paging, "ASL", required: true);
        var sending = Sending(Ana);

        Assert.False(await sending.CanSendOneOffAsync(Paging, default));
        Assert.Null(await sending.OpenOneOffAsync(Paging, default));
        var checkedSend = await sending.CheckAsync(OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", "Gate B4 is now open")), default);
        var result = await sending.SendAsync(OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", "Gate B4 is now open")), null, default);

        Assert.Equal((MessageSendOutcome.NotSendable, MessageSending.RequiredSlotRefusal), (checkedSend.Outcome, checkedSend.Problem));
        Assert.Equal(MessageSendOutcome.NotSendable, result.Outcome);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task An_optional_Asset_Slot_leaves_one_off_text_open()
    {
        await _db.SetLanguagesAsync(Paging, "eng");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: true, userId: Ana);
        await _db.AddSlotAsync(Paging, "ASL", required: false);
        var sending = Sending(Ana);

        Assert.True(await sending.CanSendOneOffAsync(Paging, default));
        Assert.Equal(MessageSendOutcome.Sent,
            (await sending.SendAsync(OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", "Gate B4 is now open")), null, default)).Outcome);
    }

    [LocalDbFact]
    public async Task One_off_text_must_fill_every_language_within_the_character_limit()
    {
        await _db.SetLanguagesAsync(Paging, "eng", "fra");
        await using (var db = _db.Director())
            await db.TblMessageTypes.Where(t => t.MessageTypeId == Paging).ExecuteUpdateAsync(s => s.SetProperty(t => t.MaxCharacters, 5));

        var result = await Sending(Admin).SendAsync(OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", "Too long")), null, default);

        Assert.Equal(MessageSendOutcome.Invalid, result.Outcome);
        Assert.Equal([MessageSending.TextField("eng"), MessageSending.TextField("fra")], result.Errors.Keys.Order());
    }

    [LocalDbFact]
    public async Task A_send_that_reaches_no_screens_is_refused()
    {
        await using (var db = _db.Director())
            await db.TblZoneDisplays.Where(zd => zd.ZoneId == CheckIn).ExecuteDeleteAsync();
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Admin).SendAsync(Canned(message, MessageSendTarget.DisplayZones(CheckIn)), null, default);

        Assert.Equal(MessageSendOutcome.NoScreens, result.Outcome);
        await using var check = _db.Director();
        Assert.False(await check.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_sender_without_page_messages_is_refused()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Ana, gtaa: false, PermissionKeys.MessagesAuthor)
            .SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default);

        Assert.Equal(MessageSendOutcome.NotAllowed, result.Outcome);
    }

    [LocalDbFact]
    public async Task Under_D02_the_built_in_Counter_type_is_not_sent_and_without_it_Counter_is_ordinary()
    {
        var counter = await _db.AddMessageAsync(Counter);
        var request = Canned(counter, MessageSendTarget.DisplayZones(GatesA)) with { MessageTypeId = Counter };

        var gtaa = Sending(Admin, gtaa: true);
        Assert.Null(await gtaa.OpenAsync(counter, default));
        Assert.False(await gtaa.CanSendOneOffAsync(Counter, default));
        Assert.Equal(MessageSendOutcome.NotSendable, (await gtaa.SendAsync(request, null, default)).Outcome);

        var product = Sending(Admin, gtaa: false);
        Assert.NotNull(await product.OpenAsync(counter, default));
        Assert.Equal(MessageSendOutcome.Sent, (await product.SendAsync(request, null, default)).Outcome);
    }

    [LocalDbFact]
    public async Task The_dialog_offers_only_what_the_senders_rules_cover_in_each_mode()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        var setup = await Sending(Ana).OpenAsync(message, default);

        Assert.Equal([GatesA], setup!.Zones.Select(z => z.ZoneId));
        Assert.Equal([GateA1, GateA2], setup.Locations.SelectMany(l => l.Displays).Select(d => d.DisplayId));
    }

    // B-35 part 2: scheduled sends (MSG-16, MSG-17, A-15) and the priority override (MSG-09).

    [LocalDbFact]
    public async Task A_scheduled_window_is_resolved_in_each_target_displays_own_time_zone()
    {
        // Gate A1 has a player in Vancouver; Gate A2 is a reference display with no device, so Messaging:DefaultTimeZone
        // (Toronto) applies.
        await _db.AddDeviceAsync("van-1", GateA1, "America/Vancouver");
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        var weekdays = new MessageWeeklyWindow(
            new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            new TimeOnly(9, 0), new TimeOnly(17, 0));
        var when = new MessageSendTiming.Scheduled(new(2026, 10, 21, 0, 0, 0), new(2026, 10, 24, 0, 0, 0), weekdays);

        var result = await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)) with { When = when }, null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.SingleAsync();
        Assert.Equal((new DateTime(2026, 10, 21, 0, 0, 0), new DateTime(2026, 10, 24, 0, 0, 0)), (activation.StartLocal!.Value, activation.EndLocal!.Value));
        Assert.Equal((new TimeOnly(9, 0), new TimeOnly(17, 0)), (activation.WindowStartTime!.Value, activation.WindowEndTime!.Value));
        Assert.Equal([false, true, true, true, true, true, false],
            new[] { activation.Sunday, activation.Monday, activation.Tuesday, activation.Wednesday, activation.Thursday, activation.Friday, activation.Saturday });
        // The instants the range spans: midnight in Toronto (04:00 UTC) to midnight in Vancouver (07:00 UTC).
        Assert.Equal(new DateTime(2026, 10, 21, 4, 0, 0), activation.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 24, 7, 0, 0), activation.EndUtc);

        var zones = await TimeZones().ForDisplaysAsync(db, [GateA1, GateA2], default);
        // 13:30 UTC on Wednesday 21 Oct is 09:30 in Toronto and 06:30 in Vancouver.
        var morning = new DateTimeOffset(2026, 10, 21, 13, 30, 0, TimeSpan.Zero);
        Assert.True(ActivationTiming.IsShowingAt(activation, morning, zones[GateA2]));
        Assert.False(ActivationTiming.IsShowingAt(activation, morning, zones[GateA1]));
        // 22:30 UTC is 18:30 in Toronto and 15:30 in Vancouver.
        var evening = new DateTimeOffset(2026, 10, 21, 22, 30, 0, TimeSpan.Zero);
        Assert.False(ActivationTiming.IsShowingAt(activation, evening, zones[GateA2]));
        Assert.True(ActivationTiming.IsShowingAt(activation, evening, zones[GateA1]));
    }

    [LocalDbFact]
    public async Task A_send_whose_end_is_before_its_start_is_refused()
    {
        var message = await _db.AddMessageAsync(Paging);
        var sending = Sending(Admin);

        var before = await sending.SendAsync(Scheduled(message, new(2026, 10, 22, 9, 0, 0), new(2026, 10, 21, 9, 0, 0)), null, default);
        var same = await sending.SendAsync(Scheduled(message, new(2026, 10, 22, 9, 0, 0), new(2026, 10, 22, 9, 0, 0)), null, default);

        Assert.Equal(MessageSendOutcome.Invalid, before.Outcome);
        Assert.Equal("The end must be after the start.", before.Errors[MessageSending.WhenField]);
        Assert.Equal(MessageSendOutcome.Invalid, same.Outcome);
        await using var db = _db.Director();
        Assert.False(await db.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_scheduled_send_that_has_ended_on_every_screen_is_refused()
    {
        var message = await _db.AddMessageAsync(Paging);

        // Now is 14:00 UTC on 20 Oct, 10:00 in Toronto: a range ending at 09:00 there has passed.
        var result = await Sending(Admin).SendAsync(Scheduled(message, new(2026, 10, 20, 8, 0, 0), new(2026, 10, 20, 9, 0, 0)), null, default);

        Assert.Equal(MessageSendOutcome.Invalid, result.Outcome);
        Assert.Contains(MessageSending.WhenField, result.Errors.Keys);
    }

    [LocalDbFact]
    public async Task A_weekly_window_needs_a_day_and_both_daily_times_that_differ()
    {
        var message = await _db.AddMessageAsync(Paging);
        var sending = Sending(Admin);
        var monday = new HashSet<DayOfWeek> { DayOfWeek.Monday };

        var noDay = await sending.CheckAsync(Scheduled(message, weekly: new MessageWeeklyWindow(new HashSet<DayOfWeek>())), default);
        var oneTime = await sending.CheckAsync(Scheduled(message, weekly: new MessageWeeklyWindow(monday, new TimeOnly(9, 0))), default);
        var sameTimes = await sending.CheckAsync(Scheduled(message, weekly: new MessageWeeklyWindow(monday, new TimeOnly(9, 0), new TimeOnly(9, 0))), default);
        var overnight = await sending.CheckAsync(Scheduled(message, weekly: new MessageWeeklyWindow(monday, new TimeOnly(22, 0), new TimeOnly(2, 0))), default);

        Assert.Equal([MessageSending.DaysField], noDay.Errors.Keys);
        Assert.Equal([MessageSending.DailyWindowField], oneTime.Errors.Keys);
        Assert.Equal([MessageSending.DailyWindowField], sameTimes.Errors.Keys);
        Assert.Equal(MessageSendOutcome.Ready, overnight.Outcome);
    }

    [LocalDbFact]
    public async Task A_display_whose_device_time_zone_does_not_resolve_uses_the_default()
    {
        await _db.AddDeviceAsync("bad-1", GateA1, "Not/AZone");
        await _db.AddDeviceAsync("van-1", GateA2, "America/Vancouver");

        await using var db = _db.Director();
        var zones = await TimeZones().ForDisplaysAsync(db, [GateA1, GateA2, Lounge], default);

        Assert.Equal("America/Toronto", zones[GateA1].Id);
        Assert.Equal("America/Vancouver", zones[GateA2].Id);
        Assert.Equal("America/Toronto", zones[Lounge].Id);
    }

    [LocalDbFact]
    public async Task Only_a_holder_of_the_priority_override_key_may_set_one()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        var request = Canned(message, MessageSendTarget.DisplayZones(GatesA)) with { PriorityOverride = -5 };

        var without = Sending(Ana);
        Assert.False((await without.OpenAsync(message, default))!.CanOverridePriority);
        var refused = await without.SendAsync(request, null, default);
        Assert.Equal(MessageSendOutcome.NotAllowed, refused.Outcome);
        await using (var db = _db.Director())
            Assert.False(await db.TblMessageActivations.AnyAsync());

        var holder = Sending(Ana, gtaa: false, PermissionKeys.PageMessages, PermissionKeys.MessagesPriorityOverride);
        Assert.True((await holder.OpenAsync(message, default))!.CanOverridePriority);
        Assert.Equal(MessageSendOutcome.Sent, (await holder.SendAsync(request, null, default)).Outcome);
        await using var check = _db.Director();
        Assert.Equal(-5, (await check.TblMessageActivations.SingleAsync()).PriorityOverride);
    }

    [LocalDbFact]
    public async Task A_send_without_a_priority_override_takes_the_types_rank()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);

        await Sending(Ana).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default);

        await using var db = _db.Director();
        Assert.Null((await db.TblMessageActivations.SingleAsync()).PriorityOverride);
    }

    // AUD-06, MSG-15: what the send put on screens is recorded with the Activation, in the same transaction.
    [LocalDbFact]
    public async Task A_send_writes_shown_version_1_with_the_type_rank_texts_and_each_slots_asset_as_sent()
    {
        await _db.SetLanguagesAsync(Paging, "eng", "fra");
        var message = await _db.AddMessageAsync(Paging);
        await _db.AddTextAsync(message, "fra", "Changement de porte");
        var asset = await _db.AddMessageAssetAsync(message, "ASL", "sign.mp4", "/MediaAssets/9/1/ab12_sign.mp4");
        _media.Write("/MediaAssets/9/1/ab12_sign.mp4", "video bytes");

        var result = await Sending(Admin).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default);

        await using var db = _db.Director();
        var version = await db.TblMessageActivationVersions.SingleAsync();
        Assert.Equal(result.MessageActivationId, version.MessageActivationId);
        Assert.Equal((1, ShownVersionReason.FirstSend, (int?)null), (version.Sequence, version.Reason, version.RevisionNumber));
        Assert.Equal((Paging, "VISUAL_PAGING", 40), (version.MessageTypeId, version.MessageTypeCode, version.EffectiveRank));
        Assert.Equal("""{"eng":"Gate change","fra":"Changement de porte"}""", version.Texts);
        // The hash is SHA-256 of the file's bytes ("video bytes"), read when the version is written.
        Assert.Equal($$"""[{"slotKey":"ASL","assetId":{{asset}},"fileName":"sign.mp4","contentHash":"96b050b919f3fca2fc8b6923537136a197ad13c583beb1438d1a12ccbc999c42"}]""",
            version.AssetSlots);
        Assert.Equal((Now, (DateTime?)null), (version.SentUtc, version.FirstServedUtc));
    }

    // INK-07: a one-off skips approval, so its first shown version is what screens show from the send.
    [LocalDbFact]
    public async Task A_one_off_send_writes_shown_version_1_with_the_typed_text_and_no_assets()
    {
        await _db.SetLanguagesAsync(Paging, "eng", "fra");

        var result = await Sending(Admin).SendAsync(
            OneOff(MessageSendTarget.DisplayZones(GatesA), ("eng", "Gate B4 is now open"), ("fra", "La porte B4 est ouverte")), null, default);

        await using var db = _db.Director();
        var version = await db.TblMessageActivationVersions.SingleAsync();
        Assert.Equal((result.MessageActivationId, 1, ShownVersionReason.FirstSend), (version.MessageActivationId, version.Sequence, version.Reason));
        Assert.Equal("""{"eng":"Gate B4 is now open","fra":"La porte B4 est ouverte"}""", version.Texts);
        Assert.Equal("[]", version.AssetSlots);
        Assert.Equal((Now, (DateTime?)null), (version.SentUtc, version.FirstServedUtc));
    }

    // MSG-09: the effective rank is the send's priority override when it has one.
    [LocalDbFact]
    public async Task A_scheduled_send_with_a_priority_override_records_the_override_as_its_rank()
    {
        var message = await _db.AddMessageAsync(Paging);

        var result = await Sending(Admin, gtaa: false, PermissionKeys.PageMessages, PermissionKeys.MessagesPriorityOverride)
            .SendAsync(Scheduled(message) with { PriorityOverride = 7 }, null, default);

        Assert.Equal(MessageSendOutcome.Sent, result.Outcome);
        await using var db = _db.Director();
        var version = await db.TblMessageActivationVersions.SingleAsync();
        Assert.Equal((result.MessageActivationId, 1, 7, Now), (version.MessageActivationId, version.Sequence, version.EffectiveRank, version.SentUtc));
    }

    [LocalDbFact]
    public async Task A_refused_send_writes_no_shown_version()
    {
        var message = await _db.AddMessageAsync(Paging, MessageStatus.Pending);

        Assert.Equal(MessageSendOutcome.NotSendable, (await Sending(Admin).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA)), null, default)).Outcome);

        await using var db = _db.Director();
        Assert.False(await db.TblMessageActivationVersions.AnyAsync());
    }

    // INK-09: each send changes what the delivery body holds, so it moves the delivery version in its own transaction.
    [LocalDbFact]
    public async Task Send_now_and_a_scheduled_send_each_move_the_delivery_version_and_a_refused_send_does_not()
    {
        var message = await _db.AddMessageAsync(Paging);
        var pending = await _db.AddMessageAsync(Paging, MessageStatus.Pending);
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        await Sending(Admin).SendAsync(Canned(message, MessageSendTarget.DisplayZones(GatesA), minutes: 15), null, default);
        var afterSendNow = await DeliveryVersionProbe.ReadAsync(_db.Director);
        await Sending(Admin).SendAsync(Scheduled(message), null, default);
        var afterScheduled = await DeliveryVersionProbe.ReadAsync(_db.Director);
        await Sending(Admin).SendAsync(Canned(pending, MessageSendTarget.DisplayZones(GatesA)), null, default);

        Assert.NotEqual(before, afterSendNow);
        Assert.NotEqual(afterSendNow, afterScheduled);
        Assert.Equal(afterScheduled, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    private MessageSending Sending(int userId, bool gtaa = false, params string[] keys)
    {
        var session = new KeySession(keys.Length == 0 ? [PermissionKeys.PageMessages] : keys)
        {
            User = new TblUser { UserId = userId, RoleId = userId == Admin ? 1 : 3 }
        };
        var d02 = Options.Create(new D02GtaaMessagingOptions { Enabled = gtaa });
        return new MessageSending(_db.Factory, new MessageSendAccess(_db.Factory), new MessageLanguageCatalogue(d02), session, d02,
            new FixedClock(Now), TimeZones(), Versions(), new MessagingApiTests.RecordingLog(), NullLogger<MessageSending>.Instance);
    }

    private ShownVersions Versions() => new(_media, NullLogger<ShownVersions>.Instance);

    private static DisplayTimeZones TimeZones() =>
        new(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }), NullLogger<DisplayTimeZones>.Instance);

    private static MessageSendRequest Scheduled(int messageId, DateTime? start = null, DateTime? end = null, MessageWeeklyWindow? weekly = null) =>
        Canned(messageId, MessageSendTarget.DisplayZones(GatesA)) with
        {
            When = new MessageSendTiming.Scheduled(start ?? new(2026, 10, 21, 0, 0, 0), end ?? new(2026, 10, 28, 0, 0, 0), weekly),
        };

    private static MessageSendRequest Canned(int messageId, MessageSendTarget target, int? minutes = null) => new()
    {
        MessageTypeId = Paging, MessageId = messageId, Target = target, When = new MessageSendTiming.SendNow(minutes),
    };

    private static MessageSendRequest OneOff(MessageSendTarget target, params (string Code, string Body)[] texts) => new()
    {
        MessageTypeId = Paging, Target = target, When = new MessageSendTiming.SendNow(null),
        OneOffTexts = texts.ToDictionary(t => t.Code, t => t.Body),
    };

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
