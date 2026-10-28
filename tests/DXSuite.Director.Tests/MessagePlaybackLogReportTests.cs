using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Director.Services.Reports;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause numbers in this file refer to docs/spec/gtaa-fsd-1.1.md section 3.6.

/// <summary>
/// B-56: the Message Playback Log report (AUD-06, AUD-13, AUD-15, AUD-18) over a Director database on LocalDB. Shown
/// versions are seeded as B-80 will write them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessagePlaybackLogReportTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    private readonly MessageAccessDatabase _db;
    private readonly RecordingEventLog _events = new(Admin);

    public MessagePlaybackLogReportTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task It_lists_Activations_sent_in_the_range_newest_first_with_their_shown_versions()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Gate change");
        var older = await _db.AddActivationAsync(message, Now.AddHours(-3), Now.AddHours(-2), [GateA1], GatesA, byUserId: Ana);
        var newer = await _db.AddActivationAsync(message, Now.AddHours(-1), null, [GateA1, GateA2], GatesA, byUserId: Bo);
        await _db.AddActivationAsync(message, Now.AddDays(-3), null, [GateA1], GatesA, cancelledUtc: Now.AddDays(-2));
        var first = await _db.AddShownVersionAsync(newer, 1, Now.AddHours(-1), new Dictionary<string, string> { ["eng"] = "Gate moved", ["fra"] = "Porte changée" },
            rank: 2, assetSlots: """[{"slotKey":"ASL","assetId":5521,"fileName":"gate-asl.mp4","contentHash":"3f9a0cb1c2"}]""",
            firstServedUtc: Now.AddHours(-1).AddSeconds(1));
        var second = await _db.AddShownVersionAsync(newer, 2, Now.AddMinutes(-20), new Dictionary<string, string> { ["eng"] = "Gate moved to A2" },
            revisionNumber: 1);

        var result = await Report(PermissionKeys.PageMessagePlaybackLog).QueryAsync(Day());

        Assert.Equal([newer, older], result.Rows.Select(r => r.ActivationId));
        Assert.Equal(2, result.TotalCount);
        var row = result.Rows[0];
        Assert.Equal(("Gate change", "Visual Paging", "Bo Duty", 2), (row.MessageName, row.MessageTypeName, row.SentBy, row.ScreenCount));
        Assert.Equal((ActivationStatus.Active, Now.AddHours(-1), (DateTime?)null), (row.Status, row.SentUtc, row.EndUtc));
        Assert.Equal(ActivationStatus.Ended, result.Rows[1].Status);
        Assert.Empty(result.Rows[1].Versions);

        Assert.Equal([first, second], row.Versions.Select(v => v.VersionId));
        var v1 = row.Versions[0];
        Assert.Equal((1, ShownVersionReason.FirstSend, "First send", "VISUAL_PAGING", 2), (v1.Sequence, v1.Reason, v1.ReasonText, v1.MessageTypeCode, v1.EffectiveRank));
        Assert.Equal([("eng", "Gate moved"), ("fra", "Porte changée")], v1.Texts.Select(t => (t.LanguageCode, t.Text)));
        var asset = Assert.Single(v1.Assets);
        Assert.Equal(("ASL", (int?)5521, "gate-asl.mp4", "3f9a0cb1c2"), (asset.SlotKey, asset.AssetId, asset.FileName, asset.ContentHash));
        Assert.Equal((Now.AddHours(-1), (DateTime?)Now.AddHours(-1).AddSeconds(1)), (v1.SentUtc, v1.FirstServedUtc));
        var v2 = row.Versions[1];
        Assert.Equal(("Revision 1", (DateTime?)null), (v2.ReasonText, v2.FirstServedUtc));
        Assert.Empty(v2.Assets);
    }

    [LocalDbFact]
    public async Task A_scheduled_send_outside_its_weekly_window_is_Scheduled_and_shows_its_screen_time_range()
    {
        // Now is Tuesday 20 Oct 2026, 10:00 on Gate A1's Toronto clock; the window opens at 18:00.
        await _db.AddDeviceAsync("dev-a1", GateA1, "America/Toronto");
        var message = await _db.AddMessageAsync(Paging);
        var timing = ActivationTiming.Resolve(
            new MessageSendTiming.Scheduled(new DateTime(2026, 10, 19, 0, 0, 0), new DateTime(2026, 11, 2, 0, 0, 0),
                new MessageWeeklyWindow(new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday }, new TimeOnly(18, 0), new TimeOnly(20, 0))),
            Now, [TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")]);
        await _db.AddActivationAsync(message, default, null, [GateA1], GatesA, sentUtc: Now.AddHours(-1), timing: timing);

        var row = Assert.Single((await Report(PermissionKeys.PageMessagePlaybackLog).QueryAsync(Day())).Rows);

        Assert.Equal(ActivationStatus.Scheduled, row.Status);
        Assert.Equal(((DateTime?)new DateTime(2026, 10, 19, 0, 0, 0), (DateTime?)new DateTime(2026, 11, 2, 0, 0, 0), "Mon, Tue 18:00–20:00"),
            (row.StartLocal, row.EndLocal, row.Window));
    }

    [LocalDbFact]
    public async Task A_Display_Zone_picked_at_send_with_no_screens_of_its_own_is_listed_and_found_by_its_filter()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddHours(-1), null, [GateA1], GatesA, alsoPicked: [CheckIn]);
        var report = Report(PermissionKeys.PageMessagePlaybackLog);

        var row = Assert.Single((await report.QueryAsync(Day())).Rows);
        Assert.Equal([(GatesA, "Gates A", 1), (CheckIn, "Check-in", 0)], row.Zones.Select(z => (z.ZoneId!.Value, z.Name, z.ScreenCount)));
        Assert.Equal([activation], (await report.QueryAsync(Day() with { ZoneId = CheckIn })).Rows.Select(r => r.ActivationId));
    }

    [LocalDbFact]
    public async Task Each_screen_keeps_its_names_and_the_Display_Zone_it_came_through_after_the_display_is_deleted()
    {
        const int Kiosk = 399;
        await using (var db = _db.Director())
            await db.Database.ExecuteSqlRawAsync($"""
                SET IDENTITY_INSERT tblDisplay ON;
                INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
                VALUES ({Kiosk}, {Terminal3}, 'Kiosk 9', 0, 1, 1, 0, 0, 1, 0);
                SET IDENTITY_INSERT tblDisplay OFF;
                """);
        var message = await _db.AddMessageAsync(Paging);
        var throughZones = await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddHours(-1), [CheckIn2, Kiosk], OldCheckIn, byUserId: Ana);
        await _db.AddActivationAsync(message, Now.AddHours(-2), Now.AddHours(-1), [GateA1, GateA2], GatesA, byUserId: Ana, sentUtc: Now.AddHours(-2).AddSeconds(1));
        await using (var db = _db.Director())
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM tblDisplay WHERE DisplayId = {Kiosk}");
        var oneDisplay = await _db.AddActivationAsync(message, Now.AddHours(-1), null, [Lounge], byUserId: Ana);
        var report = Report(PermissionKeys.PageMessagePlaybackLog);

        var rows = (await report.QueryAsync(Day())).Rows.ToDictionary(r => r.ActivationId);

        var zone = Assert.Single(rows[throughZones].Zones);
        Assert.Equal(((int?)OldCheckIn, "Old check-in", true, 2), (zone.ZoneId, zone.Name, zone.Retired, zone.ScreenCount));
        Assert.Equal(((int?)null, (string?)null, 1), (Assert.Single(rows[oneDisplay].Zones).ZoneId, rows[oneDisplay].Zones[0].Name, rows[oneDisplay].ScreenCount));
        var screens = await report.ScreensAsync(throughZones);
        Assert.Equal(
            [((int?)CheckIn2, "Check-in 2", "Terminal 3 Check-in", "Old check-in", true), (Kiosk, "Kiosk 9", "Terminal 3 Check-in", "Old check-in", true)],
            screens.Select(s => (s.DisplayId, s.DisplayName, s.LocationName, s.ZoneName, s.ZoneRetired)));
        var lounge = Assert.Single(await report.ScreensAsync(oneDisplay));
        Assert.Equal(("Lounge", "Terminal 1 Gates", (string?)null), (lounge.DisplayName, lounge.LocationName, lounge.ZoneName));
    }

    [LocalDbFact]
    public async Task A_VersionId_from_the_sheet_or_an_Activation_id_finds_its_Activation_whatever_the_range_and_filters()
    {
        // VersionIds run far ahead of Activation ids, as on a live tenant, so one number names one record here.
        await using (var db = _db.Director())
            await db.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('tblMessageActivationVersion', RESEED, 48200)");
        var message = await _db.AddMessageAsync(Paging);
        var lastYear = await _db.AddActivationAsync(message, Now.AddYears(-1), Now.AddYears(-1).AddHours(1), [GateA1], GatesA, byUserId: Ana);
        await _db.AddShownVersionAsync(lastYear, 1, Now.AddYears(-1), new Dictionary<string, string> { ["eng"] = "Old" });
        var revised = await _db.AddShownVersionAsync(lastYear, 2, Now.AddYears(-1).AddMinutes(30), new Dictionary<string, string> { ["eng"] = "Old, revised" },
            revisionNumber: 1);
        var today = await _db.AddActivationAsync(message, Now.AddHours(-1), null, [GateA1], GatesA, byUserId: Ana);
        var report = Report(PermissionKeys.PageMessagePlaybackLog);
        var narrowed = Day() with { ZoneId = CheckIn };

        Assert.Equal([lastYear], (await report.QueryAsync(narrowed with { Find = revised })).Rows.Select(r => r.ActivationId));
        Assert.Equal([lastYear], (await report.QueryAsync(narrowed with { Find = lastYear })).Rows.Select(r => r.ActivationId));
        Assert.Equal([today], (await report.QueryAsync(narrowed with { ActivationId = today })).Rows.Select(r => r.ActivationId));
        Assert.Empty((await report.QueryAsync(narrowed with { Find = 999999 })).Rows);
    }

    [LocalDbFact]
    public async Task Each_filter_narrows_the_Activations()
    {
        var paging = await _db.AddMessageAsync(Paging, name: "Paging");
        var emergency = await _db.AddMessageAsync(Emergency, name: "Evacuate");
        var byAna = await _db.AddActivationAsync(paging, Now.AddHours(-4), null, [GateA1], GatesA, byUserId: Ana);
        var byBo = await _db.AddActivationAsync(emergency, Now.AddHours(-3), null, [CheckIn1], CheckIn, byUserId: Bo);
        var fromApi = await _db.AddActivationAsync(paging, Now.AddHours(-2), null, [CheckIn2], OldCheckIn);
        await using (var db = _db.Director())
            await db.TblMessageActivations.Where(a => a.MessageActivationId == fromApi)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Source, MessageActivationSource.Api));
        var report = Report(PermissionKeys.PageMessagePlaybackLog);

        async Task<int[]> Ids(MessagePlaybackLogQuery q) => (await report.QueryAsync(q)).Rows.Select(r => r.ActivationId).Order().ToArray();

        Assert.Equal([byAna, byBo, fromApi], await Ids(Day()));
        Assert.Equal([byBo], await Ids(Day() with { MessageTypeId = Emergency }));
        Assert.Equal([fromApi], await Ids(Day() with { Source = MessageActivationSource.Api }));
        Assert.Equal([fromApi], await Ids(Day() with { ZoneId = OldCheckIn }));
        Assert.Equal([byBo], await Ids(Day() with { SentBy = "duty" }));
        Assert.Equal([byAna], await Ids(Day() with { SentBy = "ana@" }));
        Assert.Empty(await Ids(Day() with { StartUtc = Now.AddHours(-1) }));
        var options = await report.FilterOptionsAsync();
        Assert.Equal([("Gates A", false), ("Check-in", false), ("Old check-in", true)], options.DisplayZones.Select(z => (z.Name, z.Gone)));
        Assert.Contains(options.MessageTypes, t => t.Id == Emergency && t.Name == "Emergency");
    }

    [LocalDbFact]
    public async Task Showing_the_report_records_nothing_and_a_role_without_the_key_is_refused()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddHours(-1), null, [GateA1], GatesA, byUserId: Ana);

        await Report(PermissionKeys.PageMessagePlaybackLog).QueryAsync(Day());
        await Report(PermissionKeys.PageMessagePlaybackLog).ScreensAsync(activation);

        Assert.Empty(_events.Events);
        var refused = Report(PermissionKeys.PageUserEventLog, PermissionKeys.ReportsExport);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.QueryAsync(Day()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.FilterOptionsAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.ScreensAsync(activation));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.ExportAsync(Day()));
        var viewOnly = Report(PermissionKeys.PageMessagePlaybackLog);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewOnly.ExportAsync(Day()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewOnly.PrintAsync(Day()));
        Assert.Empty(_events.Events);
    }

    [LocalDbFact]
    public async Task Excel_holds_each_shown_version_and_every_screen_in_ISO_8601_UTC_and_records_report_exported()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Gate change");
        var activation = await _db.AddActivationAsync(message, Now.AddHours(-1), null, [CheckIn2], OldCheckIn, byUserId: Ana);
        var version = await _db.AddShownVersionAsync(activation, 1, Now.AddHours(-1), new Dictionary<string, string> { ["eng"] = "Gate moved", ["fra"] = "Porte changée" },
            assetSlots: """[{"slotKey":"ASL","assetId":5521,"fileName":"gate-asl.mp4","contentHash":"3f9a0cb1c2"}]""");

        var file = await Report(PermissionKeys.PageMessagePlaybackLog, PermissionKeys.ReportsExport)
            .ExportAsync(Day() with { ZoneId = OldCheckIn });

        Assert.EndsWith(".xlsx", file.FileName);
        Assert.StartsWith("MessagePlaybackLog_", file.FileName);
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(file.Bytes));
        var versions = workbook.Worksheet("Shown versions");
        var header = versions.RowsUsed().First(r => r.Cell(1).GetString() == "ActivationId");
        string Cell(ClosedXML.Excel.IXLRow row, string column) =>
            row.Cell(header.CellsUsed().First(c => c.GetString() == column).Address.ColumnNumber).GetString();
        var row = header.RowBelow();
        Assert.Equal(activation.ToString(), Cell(row, "ActivationId"));
        Assert.Equal(version.ToString(), Cell(row, "VersionId"));
        Assert.Equal("2026-10-20T13:00:00Z", Cell(row, "SentUtc"));
        Assert.Equal("", Cell(row, "DeliveredToSignageUtc"));
        Assert.Equal("Gate moved", Cell(row, "Text_eng"));
        Assert.Equal("Porte changée", Cell(row, "Text_fra"));
        Assert.Contains("gate-asl.mp4", Cell(row, "Assets"));
        Assert.Contains(versions.CellsUsed(), c => c.GetString().Contains("displayZone: Old check-in (retired)"));
        var screens = workbook.Worksheet("Screens");
        var screenHeader = screens.RowsUsed().First(r => r.Cell(1).GetString() == "ActivationId");
        Assert.Equal([activation.ToString(), CheckIn2.ToString(), "Check-in 2", "Terminal 3 Check-in", "Old check-in", "Yes"],
            screenHeader.RowBelow().Cells(1, 6).Select(c => c.GetString()));

        var exported = _events.Single(UserEventActionKeys.ReportExported);
        Assert.Equal((UserEventTargetTypes.Report, "message-playback-log"), (exported.Target!.Type, exported.Target.Id));
        var details = RecordingEventLog.Details(exported);
        Assert.Equal("message-playback-log", details.GetProperty("report").GetString());
        Assert.Equal("Old check-in", details.GetProperty("filters").GetProperty("displayZone").GetString());
        Assert.Equal("2026-10-19T14:00:00Z", details.GetProperty("range").GetProperty("startUtc").GetString());
        Assert.Equal(1, details.GetProperty("rowCount").GetInt32());
        Assert.Empty(_events.Of(UserEventActionKeys.ReportPrinted));
    }

    [LocalDbFact]
    public async Task The_print_view_states_the_find_and_who_printed_and_records_report_printed()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddDays(-30), null, [GateA1], GatesA, byUserId: Ana);
        var version = await _db.AddShownVersionAsync(activation, 1, Now.AddDays(-30), new Dictionary<string, string> { ["eng"] = "Hi" });

        var print = await Report(PermissionKeys.PageMessagePlaybackLog, PermissionKeys.ReportsExport).PrintAsync(Day() with { Find = version });

        Assert.Equal([activation], print.Result.Rows.Select(r => r.ActivationId));
        Assert.True(print.Header.RangeIgnored);
        Assert.Equal($"find: {version}", print.Header.Filters);
        Assert.Equal($"user {Admin}", print.Header.MadeBy);
        var printed = RecordingEventLog.Details(_events.Single(UserEventActionKeys.ReportPrinted));
        Assert.Equal("message-playback-log", printed.GetProperty("report").GetString());
        Assert.Equal(version.ToString(), printed.GetProperty("filters").GetProperty("find").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, printed.GetProperty("range").ValueKind);
        Assert.Empty(_events.Of(UserEventActionKeys.ReportExported));
    }

    private static MessagePlaybackLogQuery Day() => new() { StartUtc = Now.AddDays(-1), EndUtc = Now };

    private IMessagePlaybackLogReport Report(params string[] keys)
    {
        var session = new KeySession(keys) { User = new TblUser { UserId = Admin, RoleId = 3 } };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<ISessionState>(session);
        services.AddSingleton<IUserEventLog>(_events);
        services.AddSingleton(new DisplayTimeZones(
            Microsoft.Extensions.Options.Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DisplayTimeZones>.Instance));
        services.AddMessagePlaybackLogReport();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMessagePlaybackLogReport>();
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
