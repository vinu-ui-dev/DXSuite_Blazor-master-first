using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Section numbers and clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-11 (B-25) on the Flight Management page and its real forms, over a fake CoreAPI whose one server can stop
/// answering: the editor turns read-only with a banner, never enters flight Outage mode for it, queues nothing, and
/// recovers on the next call a server answers.
/// </summary>
public class CoreApiUnreachablePageTests
{
    private const string Server = "http://coreapi-unreachable.test";

    // 10:30 in Toronto (EDT): the window is 04:30 today to 04:30 tomorrow.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    private const string Arrival = """{ "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ORG": "YUL", "CAR": "3", "RCP": "ON", "ScheduledTime": "2026-10-19T10:00:00", "EstimatedTime": "2026-10-19T10:00:00", "CodeShares": [] }""";
    private const string Departure = """{ "RecordId": 1, "FLT": "WS700", "FLC": "WS", "FLN": "700", "DES": "YVR", "GAT": "A1", "RCP": "ON", "ScheduledTime": "2026-10-19T11:00:00", "EstimatedTime": "2026-10-19T11:00:00", "CodeShares": [] }""";

    private const string EverySettingOn = """{ "ETModify": true, "ATModify": true, "RCPModify": true, "GATModify": true, "CARModify": true, "ROPModify": true, "ROBModify": true, "ROSModify": true, "ROTModify": true }""";

    private static readonly string[] Fields = ["fde-et", "fde-at", "fde-status", "fde-gate", "fde-belt", "fde-public", "fde-baggage", "fde-staff", "fde-throw", "fde-keep-box"];

    private static CoreApiFeedState Watcher(string status, bool reachable, int secondsLater = 0) =>
        new(status, reachable, null, Now.UtcDateTime.AddSeconds(secondsLater));

    [Theory]
    [InlineData(false, "OK")]
    [InlineData(true, "OK")]
    [InlineData(false, "Feed stale")]
    [InlineData(true, "Feed stale")]
    public async Task When_the_watcher_finds_no_server_an_open_Manage_form_turns_read_only_with_the_banner(bool departures, string feed)
    {
        var coreApi = new FakeCoreApi { FeedStatus = feed, Settings = EverySettingOn };
        await using var page = await OpenAsync(coreApi, Watcher(feed, reachable: true));
        await ManageAsync(page, departures);
        Assert.NotEmpty(OpenFields(page.Renderer.Html));

        coreApi.Answering = false;
        await page.FeedStateReadAsync(Watcher(feed, reachable: false, secondsLater: 30));

        var html = page.Renderer.Html;
        Assert.Empty(OpenFields(html));
        Assert.Contains(CoreApiReach.Banner, html);
        Assert.Matches("<button[^>]*class=\"[^\"]*fde-save[^\"]*\"[^>]*disabled|<button[^>]*disabled[^>]*class=\"[^\"]*fde-save", html);
    }

    [Fact]
    public async Task Opening_with_no_server_answering_shows_the_banner_and_never_enters_flight_Outage_mode()
    {
        var coreApi = new FakeCoreApi { Answering = false };
        await using var page = await OpenAsync(coreApi, state: null);

        // The hub can still deliver a flight while no server answers the editor's calls.
        await page.LiveFeed.Page!.OnArrivalsAsync([Flight(Arrival)]);
        await page.Renderer.ClickAsync("fde-manage");

        var html = page.Renderer.Html;
        Assert.Contains(CoreApiReach.Banner, html);
        Assert.Empty(OpenFields(html));
        Assert.Matches("<button[^>]*class=\"[^\"]*fde-add[^\"]*\"[^>]*disabled|<button[^>]*disabled[^>]*class=\"[^\"]*fde-add", html);
    }

    [Fact]
    public async Task A_save_that_finds_no_server_shows_the_error_turns_the_editor_read_only_and_is_not_sent_again()
    {
        var coreApi = new FakeCoreApi { FeedStatus = "Feed stale" };
        await using var page = await OpenAsync(coreApi, Watcher("Feed stale", reachable: true));
        await ManageAsync(page, departures: false);
        await page.Renderer.ChangeAsync("fde-et", "2026-10-19T10:45:00");

        coreApi.Answering = false;
        await page.Renderer.ClickAsync("fde-save");

        var html = page.Renderer.Html;
        Assert.Contains("Could not save the flight: no CoreAPI server answered.", html);
        Assert.Contains(CoreApiReach.Banner, html);
        Assert.Empty(OpenFields(html));

        coreApi.Answering = true;
        await page.FeedStateReadAsync(Watcher("Feed stale", reachable: true));

        Assert.DoesNotContain(CoreApiReach.Banner, page.Renderer.Html);
        Assert.Equal(1, coreApi.ModifyAttempts);
    }

    [Fact]
    public async Task An_open_Add_form_turns_read_only_and_comes_back_with_what_the_user_typed()
    {
        var coreApi = new FakeCoreApi();
        await using var page = await OpenAsync(coreApi, Watcher("OK", reachable: true));
        await page.Renderer.ClickAsync("fde-add");
        await page.Renderer.ChangeAsync("#flightNumberInput", "555");

        coreApi.Answering = false;
        await page.FeedStateReadAsync(Watcher("OK", reachable: false, secondsLater: 30));

        var html = page.Renderer.Html;
        Assert.Contains(CoreApiReach.Banner, html);
        Assert.Matches("(?s)<fieldset[^>]*disabled[^>]*>.*id=\"flightNumberInput\"", html);
        Assert.Matches("<button[^>]*class=\"[^\"]*fde-save[^\"]*\"[^>]*disabled|<button[^>]*disabled[^>]*class=\"[^\"]*fde-save", html);

        coreApi.Answering = true;
        await page.FeedStateReadAsync(Watcher("OK", reachable: true, secondsLater: 60));

        html = page.Renderer.Html;
        Assert.DoesNotContain(CoreApiReach.Banner, html);
        Assert.DoesNotMatch("<fieldset[^>]*disabled", html);
        Assert.Contains("value=\"555\"", html);
    }

    [Fact]
    public async Task The_editor_recovers_on_the_next_call_a_server_answers_and_loads_what_it_could_not()
    {
        var coreApi = new FakeCoreApi { Answering = false, Settings = EverySettingOn };
        await using var page = await OpenAsync(coreApi, Watcher("OK", reachable: false));
        Assert.Contains(CoreApiReach.Banner, page.Renderer.Html);
        Assert.DoesNotContain("AC100", page.Renderer.Html);

        // The watcher has not polled again yet; the editor's own retry is the call that finds CoreAPI back.
        coreApi.Answering = true;
        await page.FeedStateReadAsync(Watcher("OK", reachable: false));

        var html = page.Renderer.Html;
        Assert.DoesNotContain(CoreApiReach.Banner, html);
        Assert.Contains("AC100", html);
        Assert.DoesNotMatch("<button[^>]*class=\"[^\"]*fde-add[^\"]*\"[^>]*disabled", html);
        await page.Renderer.ClickAsync("fde-manage");
        Assert.Equal(["fde-et", "fde-at", "fde-status", "fde-belt", "fde-public", "fde-baggage", "fde-staff", "fde-throw"], OpenFields(page.Renderer.Html));
    }

    [Fact]
    public async Task A_Manage_form_opened_while_unreachable_takes_the_feeds_mode_when_CoreAPI_returns()
    {
        var coreApi = new FakeCoreApi { Answering = false, Settings = """{ "ETModify": true }""" };
        await using var page = await OpenAsync(coreApi, state: null);
        await page.LiveFeed.Page!.OnArrivalsAsync([Flight(Arrival)]);
        await page.Renderer.ClickAsync("fde-manage");

        coreApi.Answering = true;
        await page.FeedStateReadAsync(null);

        // The feed is up, so the field settings decide; the form had opened before the feed status could be read.
        var html = page.Renderer.Html;
        Assert.DoesNotContain(CoreApiReach.Banner, html);
        Assert.Equal(["fde-et"], OpenFields(html));
    }

    [Fact]
    public async Task Opening_with_no_server_answering_tries_once_rather_than_once_per_list()
    {
        var coreApi = new FakeCoreApi { Answering = false };
        await using var page = await OpenAsync(coreApi, state: null);

        // The page's load, then the retry the indicator's first read makes.
        Assert.Equal(2, coreApi.Attempts);
    }

    private static FEArrivalFlight Flight(string json) =>
        JsonSerializer.Deserialize<FEArrivalFlight>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static async Task ManageAsync(OpenPage page, bool departures)
    {
        if (departures) await page.Renderer.ClickAsync("fde-departures");
        await page.Renderer.ClickAsync("fde-manage");
    }

    private static string[] OpenFields(string html) =>
        Fields.Where(f => Regex.IsMatch(html, $"<(input|select)(?![^>]* disabled)[^>]*class=\"[^\"]*\\b{f}\\b[^\"]*\"[^>]*>")).ToArray();

    private static async Task<OpenPage> OpenAsync(FakeCoreApi coreApi, CoreApiFeedState? state)
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
        services.AddSingleton(Options.Create(new FlightDataEditorOptions()));
        services.AddFlightEditAudit();
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => coreApi);

        var renderer = await InteractiveRenderer.RenderAsync<FlightManagement>(new Dictionary<string, object?>(), services.BuildServiceProvider());
        return new OpenPage(renderer, liveFeed);
    }

    private sealed record OpenPage(InteractiveRenderer Renderer, CapturingLiveFeed LiveFeed) : IAsyncDisposable
    {
        /// <summary>What the RMS Feed indicator does with each read of the watcher's state.</summary>
        public Task FeedStateReadAsync(CoreApiFeedState? state) => ((FlightManagement)LiveFeed.Page!).FeedStateReadAsync(state);

        public ValueTask DisposeAsync()
        {
            Renderer.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Answers the routes the page loads, or, while <see cref="Answering"/> is false, refuses every connection as a
    /// server that is down does.
    /// </summary>
    private sealed class FakeCoreApi : HttpMessageHandler
    {
        public volatile bool Answering = true;

        public string FeedStatus { get; init; } = "OK";

        public string Settings { get; init; } = "{}";

        private int _attempts;
        private int _modifyAttempts;

        public int Attempts => _attempts;

        public int ModifyAttempts => _modifyAttempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Interlocked.Increment(ref _attempts);
            if (path.Contains("/Modify/")) Interlocked.Increment(ref _modifyAttempts);
            if (!Answering) throw new HttpRequestException("Connection refused.");
            var json = path switch
            {
                "Override/Arrivals" => $"[{Arrival}]",
                "Override/Departures" => $"[{Departure}]",
                "Override/FeedStatus" => $$"""{ "Status": "{{FeedStatus}}" }""",
                "ApiKey/ApiKeyID" => "\"key-1\"",
                "Override/GetSettings/key-1" => Settings,
                "Override/Airlines" => """[ { "AirlineCode": "AC", "CarrierName": "Air Canada" }, { "AirlineCode": "WS", "CarrierName": "WestJet" } ]""",
                "Override/Locations" => """[ { "IataCode": "YUL", "Name": "Montreal", "Country": "Canada", "LanguageCodes": "" }, { "IataCode": "YVR", "Name": "Vancouver", "Country": "Canada", "LanguageCodes": "" } ]""",
                "Override/Statuses" => """[ { "StatusId": "ON", "StatusName": "On time" }, { "StatusId": "DL", "StatusName": "Delayed" } ]""",
                "Override/Gates" => """[ { "Gat": "A1" }, { "Gat": "B2" } ]""",
                "Override/Carousels" => """[ { "CarouselId": "3", "BeltRelayIp": "" }, { "CarouselId": "4", "BeltRelayIp": "" } ]""",
                _ when path.Contains("/Modify/") => """{ "recordID": 1, "overrideResponse": "OK", "preserveOverride": true }""",
                _ when path.EndsWith("/Add") => "2",
                _ => "[]",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
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

    private sealed class StubFeedState(CoreApiFeedState? state) : ICoreApiFeedStateReader
    {
        public Task<CoreApiFeedState?> ReadAsync(CancellationToken ct) => Task.FromResult(state);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
