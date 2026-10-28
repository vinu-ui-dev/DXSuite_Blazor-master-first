using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Section numbers and clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-03 and FDE-04 (B-22) on the Flight Management page over a fake CoreAPI: the flight Outage mode switch shows
/// only where the instance allows it, and Manage saves the kind the feed mode and the "Keep after the feed returns"
/// box decide.
/// </summary>
public class FlightEditKindPageTests
{
    private const string Server = "http://coreapi-kinds.test";
    private const string Box = "fde-keep-box";
    private const string Switch = "fde-outage-switch";

    // 10:30 in Toronto (EDT): the window is 04:30 today to 04:30 tomorrow.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    private const string PlainFlight = """{ "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ScheduledTime": "2026-10-19T10:00:00", "EstimatedTime": "2026-10-19T10:00:00", "ActualTime": "2026-10-19T10:00:00", "CodeShares": [] }""";
    private const string OutageEdit = """{ "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ScheduledTime": "2026-10-19T10:00:00", "EstimatedTime": "2026-10-19T10:00:00", "ActualTime": "2026-10-19T10:00:00", "CodeShares": [], "overrideFlightData": { "ROS": "N" }, "preserveOverride": false }""";
    private const string PreservedEdit = """{ "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ScheduledTime": "2026-10-19T10:00:00", "EstimatedTime": "2026-10-19T10:00:00", "ActualTime": "2026-10-19T10:00:00", "CodeShares": [], "overrideFlightData": { "ROS": "N" }, "preserveOverride": true }""";

    private static readonly CoreApiFeedState Up = new("OK", true, null, Now.UtcDateTime);
    private static readonly CoreApiFeedState Down = new("Feed stale", true, null, Now.UtcDateTime);

    [Fact]
    public async Task With_the_feed_trusted_Manage_shows_no_box_and_saves_a_leftover_Outage_Edit_as_Preserved()
    {
        var coreApi = new FakeCoreApi(OutageEdit);
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-manage");
        Assert.DoesNotContain(KeepAfterFeedReturns.Label, page.Renderer.Html);
        await page.Renderer.ClickAsync("fde-save");

        Assert.Contains("\"Preserved\":true", Assert.Single(coreApi.ModifyBodies));
    }

    [Fact]
    public async Task During_flight_Outage_mode_Manage_opens_the_box_unticked_and_an_unticked_save_is_an_Outage_Edit()
    {
        var coreApi = new FakeCoreApi(PlainFlight, feedStatus: "Feed stale");
        await using var page = await OpenAsync(coreApi, Down);

        await page.Renderer.ClickAsync("fde-manage");
        Assert.Contains(KeepAfterFeedReturns.Label, page.Renderer.Html);
        Assert.False(BoxTicked(page.Renderer.Html));
        await page.Renderer.ClickAsync("fde-save");

        Assert.Contains("\"Preserved\":false", Assert.Single(coreApi.ModifyBodies));
    }

    [Fact]
    public async Task During_flight_Outage_mode_ticking_the_box_saves_a_Preserved_Edit()
    {
        var coreApi = new FakeCoreApi(OutageEdit, feedStatus: "Feed stale");
        await using var page = await OpenAsync(coreApi, Down);

        await page.Renderer.ClickAsync("fde-manage");
        Assert.False(BoxTicked(page.Renderer.Html));
        await page.Renderer.ChangeAsync(Box, true);
        await page.Renderer.ClickAsync("fde-save");

        Assert.Contains("\"Preserved\":true", Assert.Single(coreApi.ModifyBodies));
    }

    [Fact]
    public async Task During_flight_Outage_mode_a_Preserved_Edit_opens_ticked_and_locked()
    {
        var coreApi = new FakeCoreApi(PreservedEdit, feedStatus: "Feed stale");
        await using var page = await OpenAsync(coreApi, Down);

        await page.Renderer.ClickAsync("fde-manage");
        var html = page.Renderer.Html;

        Assert.True(BoxTicked(html));
        Assert.Matches($"<input[^>]*class=\"[^\"]*{Box}[^\"]*\"[^>]* disabled", html);
        Assert.Contains(KeepAfterFeedReturns.LockedNote, html);
        await page.Renderer.ClickAsync("fde-save");
        Assert.Contains("\"Preserved\":true", Assert.Single(coreApi.ModifyBodies));
    }

    [Fact]
    public async Task The_switch_is_not_shown_where_the_instance_does_not_allow_it_and_a_reported_switch_is_ignored()
    {
        var coreApi = new FakeCoreApi(PlainFlight);
        await using var page = await OpenAsync(coreApi, Up with { SwitchOn = true }, allowSwitch: false);

        Assert.DoesNotContain(Switch, page.Renderer.Html);
        await page.Renderer.ClickAsync("fde-manage");
        Assert.DoesNotContain(KeepAfterFeedReturns.Label, page.Renderer.Html);
    }

    [Fact]
    public async Task Where_allowed_the_switch_is_CoreAPIs_flag_and_switching_it_on_puts_the_flights_in_Outage_mode()
    {
        var coreApi = new FakeCoreApi(PlainFlight);
        await using var page = await OpenAsync(coreApi, Up, allowSwitch: true);
        Assert.Contains(Switch, page.Renderer.Html);

        await page.Renderer.ChangeAsync(Switch, true);
        await page.Renderer.ClickAsync("fde-manage");

        Assert.Equal("True", Assert.Single(coreApi.ToggleStates));
        Assert.Contains(KeepAfterFeedReturns.Label, page.Renderer.Html);
    }

    [Fact]
    public async Task Where_allowed_the_switch_follows_the_watchers_state_and_the_hub()
    {
        var coreApi = new FakeCoreApi(PlainFlight);
        await using var page = await OpenAsync(coreApi, Up with { SwitchOn = true }, allowSwitch: true);
        Assert.Matches($"<input[^>]*class=\"[^\"]*{Switch}[^\"]*\"[^>]* checked", page.Renderer.Html);

        await page.LiveFeed.Page!.OnOutageSwitchAsync(false);
        await page.Renderer.ClickAsync("fde-manage");

        Assert.DoesNotMatch($"<input[^>]*class=\"[^\"]*{Switch}[^\"]*\"[^>]* checked", page.Renderer.Html);
        Assert.DoesNotContain(KeepAfterFeedReturns.Label, page.Renderer.Html);
        Assert.Empty(coreApi.ToggleStates);
    }

    [Fact]
    public async Task A_failed_switch_is_reported_and_leaves_the_mode_as_it_was()
    {
        var coreApi = new FakeCoreApi(PlainFlight) { ToggleFails = true };
        await using var page = await OpenAsync(coreApi, Up, allowSwitch: true);

        await page.Renderer.ChangeAsync(Switch, true);

        Assert.Contains("Could not switch flight Outage mode", page.Renderer.Html);
        await page.Renderer.ClickAsync("fde-manage");
        Assert.DoesNotContain(KeepAfterFeedReturns.Label, page.Renderer.Html);
    }

    private static bool BoxTicked(string html)
    {
        var box = System.Text.RegularExpressions.Regex.Match(html, $"<input[^>]*class=\"[^\"]*{Box}[^\"]*\"[^>]*>");
        Assert.True(box.Success, "The box is not on the form.");
        return box.Value.Contains(" checked");
    }

    private static async Task<OpenPage> OpenAsync(FakeCoreApi coreApi, CoreApiFeedState state, bool allowSwitch = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FlightListClock.AirportTimeZoneKey] = "America/Toronto" })
            .Build();
        var liveFeed = new CapturingLiveFeed();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(new KeySession(PermissionKeys.PageFlightDataEditor));
        services.AddSingleton<NavigationManager, TestNavigationManager>();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICoreApiSettingsStore>(new CoreApiClientTests.FakeSettingsStore(Server));
        services.AddSingleton<ICoreApiLiveFeed>(liveFeed);
        services.AddSingleton<ICoreApiFeedStateReader>(new StubFeedState(state));
        services.AddSingleton<IFlightListClock>(new FlightListClock(config, new FixedTime(Now)));
        services.AddSingleton(Options.Create(new FlightDataEditorOptions { AllowOutageModeSwitch = allowSwitch }));
        services.AddFlightEditAudit();
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => coreApi);

        var renderer = await InteractiveRenderer.RenderAsync<FlightManagement>(new Dictionary<string, object?>(), services.BuildServiceProvider());
        return new OpenPage(renderer, liveFeed);
    }

    private sealed record OpenPage(InteractiveRenderer Renderer, CapturingLiveFeed LiveFeed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Renderer.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Answers the routes the page loads, records each Flight Edit's body and each switch call.</summary>
    private sealed class FakeCoreApi(string arrival, string feedStatus = "OK") : HttpMessageHandler
    {
        public ConcurrentQueue<string> ModifyBodies { get; } = new();

        public ConcurrentQueue<string> ToggleStates { get; } = new();

        public bool ToggleFails { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (path == "Override/OverrideToggle")
            {
                if (ToggleFails) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                ToggleStates.Enqueue(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query)["state"].ToString());
            }
            if (path == "Override/Arrivals/Modify/1") ModifyBodies.Enqueue(await request.Content!.ReadAsStringAsync(ct));
            var json = path switch
            {
                "Override/Arrivals" => $"[{arrival}]",
                "Override/FeedStatus" => $$"""{ "Status": "{{feedStatus}}" }""",
                "Override/OverrideToggle" => """{ "feedOverride": true }""",
                "Override/Arrivals/Modify/1" => """{ "recordID": 1, "overrideResponse": "OK", "preserveOverride": true }""",
                "ApiKey/ApiKeyID" => "\"key-1\"",
                _ => "[]",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CapturingLiveFeed : ICoreApiLiveFeed
    {
        public ICoreApiHubObserver? Page { get; private set; }

        public Task<IAsyncDisposable> SubscribeAsync(ICoreApiHubObserver page, CancellationToken ct = default)
        {
            Page = page;
            return Task.FromResult<IAsyncDisposable>(new Nothing());
        }

        private sealed class Nothing : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StubFeedState(CoreApiFeedState state) : ICoreApiFeedStateReader
    {
        public Task<CoreApiFeedState?> ReadAsync(CancellationToken ct) => Task.FromResult<CoreApiFeedState?>(state);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
