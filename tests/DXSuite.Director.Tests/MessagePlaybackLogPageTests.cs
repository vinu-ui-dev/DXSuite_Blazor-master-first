using System.Reflection;
using DXSuite.Director.Entities;
using DXSuite.Director.Pages.Reports;
using DXSuite.Director.Services.Messaging;
using DXSuite.Director.Services.Reports;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace DXSuite.Director.Tests;

// Clause numbers in this file refer to docs/spec/gtaa-fsd-1.1.md section 3.6.

/// <summary>
/// B-56: the Message Playback Log screen (AUD-12, AUD-13, AUD-17, AUD-18), driven through <see cref="InteractiveRenderer"/>
/// from a browser in Toronto. The report service is a stand-in that records each query, so a test sees exactly what
/// the page asked for and when.
/// </summary>
public class MessagePlaybackLogPageTests
{
    // 09:00 in Toronto, which is five hours behind UTC in January.
    private static readonly DateTime Winter = new(2025, 1, 15, 14, 0, 0, DateTimeKind.Utc);

    private static readonly PlaybackActivation Weather = new(
        10471, 233, "Weather delays", false, "IRROPS", MessageActivationSource.DirectorAdmin, "Marc Tremblay",
        Winter, Winter, Winter.AddHours(5), null, null, ActivationStatus.Ended, 3,
        [new PlaybackZone(1, "T1", false, 2), new PlaybackZone(9, "T1 Pier F (old)", true, 1)],
        [
            new PlaybackVersion(48201, 1, ShownVersionReason.FirstSend, null, "IRROPS", 2,
                [new PlaybackText("eng", "Weather delays are affecting some flights."), new PlaybackText("fra", "Des retards dus à la météo.")],
                [new PlaybackAsset("ASL", 5521, "irrops-weather-asl.mp4", "3f9a0cb1c2")], Winter, Winter.AddSeconds(1)),
            new PlaybackVersion(48219, 2, ShownVersionReason.Revision, 1, "IRROPS", 2,
                [new PlaybackText("eng", "Check your flight status with your airline.")], [], Winter.AddMinutes(88), null),
        ]);

    [Fact]
    public void The_Reports_menu_holds_the_Message_Playback_Log_behind_its_key_after_the_User_Event_Log()
    {
        var reports = new DirectorModule().GetNavItems().Where(n => n.Group == "Reports").OrderBy(n => n.Order)
            .Select(n => (n.Label, n.Href, n.Permission)).ToList();

        Assert.Equal(
        [
            ("User Event Log", UserEventLog.Route, PermissionKeys.PageUserEventLog),
            ("Message Playback Log", MessagePlaybackLog.Route, PermissionKeys.PageMessagePlaybackLog),
            ("Content Log", "/content-log", PermissionKeys.PageContentLog),
        ], reports);
        Assert.Equal(MessagePlaybackLog.Route, typeof(MessagePlaybackLog).GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public async Task A_role_without_the_key_is_refused_before_anything_loads()
    {
        var session = new KeySession(PermissionKeys.PageUserEventLog, PermissionKeys.ReportsExport);

        var rendered = await PageRenderer.RenderAsync(
            PermissionGatedPagesTests.ThrowingServices(typeof(MessagePlaybackLog), session), typeof(MessagePlaybackLog));

        Assert.Contains("You do not have permission to access this page.", rendered);
        Assert.Contains(PermissionKeys.PageMessagePlaybackLog, session.Asked);
    }

    [Fact]
    public async Task It_opens_on_today_in_the_viewers_zone_and_shows_sent_times_with_their_offset()
    {
        var report = new RecordingReport(Weather);

        using var page = await RenderAsync(report, [PermissionKeys.PageMessagePlaybackLog]);

        var query = Assert.Single(report.Queries);
        Assert.False(query.IgnoresRange);
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        // The page's clock reads 14:00 UTC on 20 October 2026, 10:00 in Toronto.
        var today = new DateTime(2026, 10, 20);
        Assert.Equal(today, TimeZoneInfo.ConvertTimeFromUtc(query.StartUtc, toronto));
        Assert.Equal(today.AddDays(1).AddSeconds(-1), TimeZoneInfo.ConvertTimeFromUtc(query.EndUtc, toronto));
        var row = Row(page.Html, 10471);
        Assert.Contains("2025-01-15 09:00:00", row);
        Assert.Contains("-05:00", row);
        Assert.Contains("Weather delays", row);
        Assert.Contains("IRROPS", row);
        Assert.Contains("Marc Tremblay", row);
        Assert.Contains(">Ended<", row);
        Assert.DoesNotContain("mpl-detail-10471", page.Html);
        Assert.DoesNotContain("Display filter", page.Html);
        Assert.DoesNotContain("style=", page.Html);
    }

    [Fact]
    public async Task A_row_expands_in_place_to_its_shown_versions_and_the_screens_it_was_sent_to()
    {
        var report = new RecordingReport(Weather)
        {
            Screens =
            [
                new PlaybackScreen(10471, 2101, "T1-A-CK01", "T1 Check-in Aisle A", 1, "T1", false),
                new PlaybackScreen(10471, 2102, "T1-A-CK02", "T1 Check-in Aisle A", 1, "T1", false),
                new PlaybackScreen(10471, null, "Gate F12", "T1 Pier F", 9, "T1 Pier F (old)", true),
            ],
        };
        using var page = await RenderAsync(report, [PermissionKeys.PageMessagePlaybackLog]);

        await page.ClickAsync("#mpl-row-10471");

        var detail = Detail(page.Html, 10471);
        var first = Between(detail, "id=\"mpl-version-48201\"", "id=\"mpl-version-48219\"");
        Assert.Contains("First send", first);
        Assert.Contains("IRROPS, rank 2", first);
        Assert.Contains("Weather delays are affecting some flights.", first);
        Assert.Contains("Des retards dus à la météo.", first);
        Assert.Contains("irrops-weather-asl.mp4", first);
        Assert.Contains("2025-01-15 09:00:00", first);
        Assert.Contains("2025-01-15 09:00:01", first);
        var second = Between(detail, "id=\"mpl-version-48219\"", "id=\"mpl-screens-10471\"");
        Assert.Contains("Revision 1", second);
        Assert.Contains("Not delivered", second);
        var screens = detail[detail.IndexOf("id=\"mpl-screens-10471\"", StringComparison.Ordinal)..];
        Assert.Contains("Sent to 3 screens", screens);
        Assert.Contains("T1-A-CK02", screens);
        Assert.Contains("T1 Check-in Aisle A", screens);
        Assert.Contains("Gate F12", screens);
        Assert.Contains("T1 Pier F (old) (retired)", screens);
        Assert.Equal([10471], report.ScreensAsked);
        Assert.Empty(report.Exports);

        await page.ClickAsync("#mpl-row-10471");
        Assert.DoesNotContain("mpl-detail-10471", page.Html);
    }

    [Fact]
    public async Task A_VersionId_in_the_URL_finds_its_Activation_and_opens_it()
    {
        var report = new RecordingReport(Weather);

        using var page = await RenderAsync(report, [PermissionKeys.PageMessagePlaybackLog], $"{MessagePlaybackLog.Route}?find=48219");

        var query = Assert.Single(report.Queries);
        Assert.Equal(48219, query.Find);
        Assert.Contains("id=\"mpl-detail-10471\"", page.Html);
        Assert.Contains("found by 48219", page.Html);
    }

    [Fact]
    public async Task Find_runs_the_typed_VersionId_or_Activation_id()
    {
        var report = new RecordingReport(Weather);
        using var page = await RenderAsync(report, [PermissionKeys.PageMessagePlaybackLog]);

        await page.InputAsync("mpl-find-input", " 48201 ");
        await page.ClickAsync("mpl-find");

        Assert.Equal(48201, report.Queries[^1].Find);
        Assert.Contains("id=\"mpl-detail-10471\"", page.Html);
    }

    [Fact]
    public async Task A_User_Event_Log_link_opens_its_Activation()
    {
        var report = new RecordingReport(Weather);

        using var page = await RenderAsync(report, [PermissionKeys.PageMessagePlaybackLog], $"{MessagePlaybackLog.Route}?activation=10471");

        Assert.Equal(10471, Assert.Single(report.Queries).ActivationId);
        Assert.Contains("id=\"mpl-detail-10471\"", page.Html);
        var link = UserEventRecordLinks.For(UserEventTargetTypes.Activation, "10471");
        Assert.Equal(($"{MessagePlaybackLog.Route}?activation=10471", PermissionKeys.PageMessagePlaybackLog), (link?.Href, link?.PermissionKey));
    }

    [Fact]
    public async Task Show_in_the_User_Event_Log_appears_only_for_holders_of_its_key()
    {
        using var without = await RenderAsync(new RecordingReport(Weather), [PermissionKeys.PageMessagePlaybackLog], $"{MessagePlaybackLog.Route}?activation=10471");
        using var with = await RenderAsync(new RecordingReport(Weather),
            [PermissionKeys.PageMessagePlaybackLog, PermissionKeys.PageUserEventLog], $"{MessagePlaybackLog.Route}?activation=10471");

        Assert.DoesNotContain("Show in the User Event Log", without.Html);
        Assert.Contains($"href=\"{UserEventLog.Route}?targetType=Activation&target=10471\"", with.Html);
        Assert.Contains("Show in the User Event Log", with.Html);
    }

    [Fact]
    public async Task Excel_and_print_are_offered_only_with_the_export_key_and_run_the_query_on_screen()
    {
        using var without = await RenderAsync(new RecordingReport(Weather), [PermissionKeys.PageMessagePlaybackLog]);
        var report = new RecordingReport(Weather);
        using var with = await RenderAsync(report, [PermissionKeys.PageMessagePlaybackLog, PermissionKeys.ReportsExport], $"{MessagePlaybackLog.Route}?find=48219");

        Assert.DoesNotContain("EXCEL", without.Html);
        Assert.DoesNotContain("PRINT", without.Html);
        await with.ClickAsync("mpl-excel");
        Assert.Equal(48219, Assert.Single(report.Exports).Find);
        await with.ClickAsync("mpl-print");
        Assert.Equal(48219, Assert.Single(report.Prints).Find);
        Assert.Contains("Print view", with.Html);
        Assert.Contains("Range", with.Html);
        Assert.Contains("none: found by id", with.Html);
        Assert.Contains("find: 48219", with.Html);
        Assert.Contains("48219", with.Html);
        Assert.Contains("Not delivered", with.Html);
    }

    private static string Row(string html, int activationId) =>
        Between(html, $"id=\"mpl-row-{activationId}\"", "id=\"mpl-");

    private static string Detail(string html, int activationId)
    {
        var start = html.IndexOf($"id=\"mpl-detail-{activationId}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Activation {activationId} is not expanded.");
        return html[start..];
    }

    private static string Between(string html, string from, string to)
    {
        var start = html.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {from}.");
        var end = html.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    private static Task<InteractiveRenderer> RenderAsync(RecordingReport report, string[] keys, string? uri = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(new KeySession(keys));
        services.AddSingleton<IMessagePlaybackLogReport>(report);
        services.AddSingleton<IJSRuntime>(new TorontoBrowser());
        services.AddSingleton<NavigationManager>(new AtUri(uri ?? MessagePlaybackLog.Route));
        services.AddSingleton<TimeProvider>(new FixedClock(new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc)));
        return InteractiveRenderer.RenderAsync<MessagePlaybackLog>(new Dictionary<string, object?>(), services.BuildServiceProvider());
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private sealed class RecordingReport(params PlaybackActivation[] rows) : IMessagePlaybackLogReport
    {
        public List<MessagePlaybackLogQuery> Queries { get; } = new();

        public List<MessagePlaybackLogQuery> Exports { get; } = new();

        public List<MessagePlaybackLogQuery> Prints { get; } = new();

        public List<int> ScreensAsked { get; } = new();

        public IReadOnlyList<PlaybackScreen> Screens { get; init; } = [];

        public Task<MessagePlaybackLogFilterOptions> FilterOptionsAsync(CancellationToken ct = default) =>
            Task.FromResult(new MessagePlaybackLogFilterOptions(
                [new MessagePlaybackLogOption(2, "IRROPS", false)], [new MessagePlaybackLogOption(9, "T1 Pier F (old)", true)]));

        public Task<MessagePlaybackLogResult> QueryAsync(MessagePlaybackLogQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(new MessagePlaybackLogResult(rows, rows.Length));
        }

        public Task<IReadOnlyList<PlaybackScreen>> ScreensAsync(int activationId, CancellationToken ct = default)
        {
            ScreensAsked.Add(activationId);
            return Task.FromResult(Screens);
        }

        public Task<ReportFile> ExportAsync(MessagePlaybackLogQuery query, CancellationToken ct = default)
        {
            Exports.Add(query);
            return Task.FromResult(new ReportFile([1, 2, 3], "MessagePlaybackLog.xlsx", rows.Length));
        }

        public Task<MessagePlaybackLogPrint> PrintAsync(MessagePlaybackLogQuery query, CancellationToken ct = default)
        {
            Prints.Add(query);
            return Task.FromResult(new MessagePlaybackLogPrint(new MessagePlaybackLogResult(rows, rows.Length),
                new ReportHeader("Greater Toronto Airports Authority", query.StartUtc, query.EndUtc, $"find: {query.Find}", "Sam Ortiz",
                    Winter, query.IgnoresRange)));
        }
    }

    private sealed class AtUri : NavigationManager
    {
        public AtUri(string relative) => Initialize("https://director.test/", "https://director.test" + relative);

        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }
}
