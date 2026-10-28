using System.Collections.Concurrent;
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
/// FDE-05 and FDE-06 (B-24) on the Flight Management page and its real Manage and Add forms, over a fake CoreAPI:
/// which fields are open in each feed state, what a save sends, the warning when the hub changes an open flight,
/// and the error a failed save shows.
/// </summary>
public class FlightManagePageTests
{
    private const string Server = "http://coreapi-manage.test";

    // 10:30 in Toronto (EDT): the window is 04:30 today to 04:30 tomorrow.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    private const string Arrival = """{ "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ORG": "YUL", "CAR": "3", "RCP": "ON", "ScheduledTime": "2026-10-19T10:00:00", "EstimatedTime": "2026-10-19T10:00:00", "CodeShares": [ { "FLC": "LH", "FLN": "6801" } ] }""";
    private const string Departure = """{ "RecordId": 1, "FLT": "WS700", "FLC": "WS", "FLN": "700", "DES": "YVR", "GAT": "A1", "RCP": "ON", "ScheduledTime": "2026-10-19T11:00:00", "EstimatedTime": "2026-10-19T11:00:00", "CodeShares": [ { "FLC": "DL", "FLN": "4100" } ] }""";

    private static readonly CoreApiFeedState Up = new("OK", true, null, Now.UtcDateTime);
    private static readonly CoreApiFeedState Down = new("Feed stale", true, null, Now.UtcDateTime);

    private static readonly string[] DepartureFields = ["fde-et", "fde-at", "fde-status", "fde-gate", "fde-public", "fde-baggage", "fde-staff", "fde-throw"];
    private static readonly string[] ArrivalFields = ["fde-et", "fde-at", "fde-status", "fde-belt", "fde-public", "fde-baggage", "fde-staff", "fde-throw"];

    [Theory]
    [InlineData(false, "ETModify", "fde-et")]
    [InlineData(false, "ATModify", "fde-at")]
    [InlineData(false, "RCPModify", "fde-status")]
    [InlineData(false, "CARModify", "fde-belt")]
    [InlineData(false, "GATModify", null)]
    [InlineData(false, "ROPModify", "fde-public")]
    [InlineData(false, "ROBModify", "fde-baggage")]
    [InlineData(false, "ROSModify", "fde-staff")]
    [InlineData(false, "ROTModify", "fde-throw")]
    [InlineData(true, "ETModify", "fde-et")]
    [InlineData(true, "ATModify", "fde-at")]
    [InlineData(true, "RCPModify", "fde-status")]
    [InlineData(true, "GATModify", "fde-gate")]
    [InlineData(true, "CARModify", null)]
    [InlineData(true, "ROPModify", "fde-public")]
    [InlineData(true, "ROBModify", "fde-baggage")]
    [InlineData(true, "ROSModify", "fde-staff")]
    [InlineData(true, "ROTModify", "fde-throw")]
    public async Task While_the_feed_is_trusted_each_field_setting_opens_its_field_alone_for_arrivals_and_departures(
        bool departures, string setting, string? field)
    {
        var coreApi = new FakeCoreApi { Settings = $$"""{ "{{setting}}": true }""" };
        await using var page = await OpenAsync(coreApi, Up);

        await ManageAsync(page, departures);

        Assert.Equal(field is null ? [] : [field], OpenFields(page.Renderer.Html, departures ? DepartureFields : ArrivalFields));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task During_flight_Outage_mode_every_field_is_open_whatever_the_settings(bool departures)
    {
        var coreApi = new FakeCoreApi { FeedStatus = "Feed stale" };
        await using var page = await OpenAsync(coreApi, Down);

        await ManageAsync(page, departures);

        var fields = departures ? DepartureFields : ArrivalFields;
        Assert.Equal(fields, OpenFields(page.Renderer.Html, fields));
    }

    [Theory]
    [InlineData(false, "500")]
    [InlineData(true, "500")]
    [InlineData(false, "no status")]
    [InlineData(true, "no status")]
    public async Task A_feed_status_not_known_is_not_Outage_mode_and_the_settings_still_gate_the_fields(bool departures, string answer)
    {
        // CoreAPI answered the feed status with an error or without a status, and the watcher has not reported.
        var coreApi = new FakeCoreApi { FeedStatusAnswer = answer == "500" ? null : "{}", Settings = """{ "ETModify": true }""" };
        await using var page = await OpenAsync(coreApi, state: null);

        await ManageAsync(page, departures);

        var html = page.Renderer.Html;
        Assert.Equal(["fde-et"], OpenFields(html, departures ? DepartureFields : ArrivalFields));
        Assert.DoesNotContain("fde-keep-box", html);
        Assert.Contains("RMS feed state not known", html);
    }

    [Fact]
    public async Task A_watcher_state_without_a_status_is_not_Outage_mode_and_shows_as_not_known()
    {
        var coreApi = new FakeCoreApi { FeedStatusAnswer = "{}", Settings = """{ "ETModify": true }""" };
        await using var page = await OpenAsync(coreApi, new CoreApiFeedState(null, true, null, Now.UtcDateTime));

        await ManageAsync(page, departures: false);

        var html = page.Renderer.Html;
        Assert.Equal(["fde-et"], OpenFields(html, ArrivalFields));
        Assert.Contains("RMS feed state not known", html);
        Assert.DoesNotContain("RMS feed down", html);
    }

    [Fact]
    public async Task A_save_sends_only_the_changed_field_and_the_kind()
    {
        var coreApi = new FakeCoreApi { Settings = """{ "ETModify": true, "CARModify": true }""" };
        await using var page = await OpenAsync(coreApi, Up);

        await ManageAsync(page, departures: false);
        await page.Renderer.ChangeAsync("fde-et", "2026-10-19T10:45:00");
        await page.Renderer.ClickAsync("fde-save");

        Assert.Equal("""{"ETA":"2026-10-19T10:45:00","Preserved":true}""", Assert.Single(coreApi.ModifyBodies));
        Assert.DoesNotContain("fde-save", page.Renderer.Html);
    }

    [Fact]
    public async Task On_an_existing_flight_destination_and_codeshares_are_shown_read_only_and_never_sent()
    {
        var coreApi = new FakeCoreApi();
        await using var page = await OpenAsync(coreApi, Up);

        await ManageAsync(page, departures: true);
        var html = page.Renderer.Html;
        var start = html.IndexOf("fde-codeshares", StringComparison.Ordinal);
        var codeShares = html[start..html.IndexOf("dx-modal-footer", start, StringComparison.Ordinal)];

        Assert.Contains("Delta", codeShares);
        Assert.Contains("4100", codeShares);
        Assert.DoesNotContain("<input", codeShares);
        Assert.DoesNotContain("<button", codeShares);
        Assert.Contains("Vancouver, Canada", html);
        Assert.DoesNotContain("destinationInput", html);
        await page.Renderer.ClickAsync("fde-save");
        Assert.Equal("""{"Preserved":true}""", Assert.Single(coreApi.ModifyBodies));
    }

    [Fact]
    public async Task A_hub_change_to_the_open_flight_warns_and_reloads_the_form_before_saving()
    {
        var coreApi = new FakeCoreApi { Settings = """{ "ETModify": true }""" };
        await using var page = await OpenAsync(coreApi, Up);
        await ManageAsync(page, departures: false);
        await page.Renderer.ChangeAsync("fde-et", "2026-10-19T10:45:00");

        // An update that changes nothing the form shows, or another flight, leaves the form alone.
        await page.LiveFeed.Page!.OnArrivalsAsync([Flight(Arrival)]);
        await page.LiveFeed.Page!.OnArrivalsAsync([Flight(Arrival.Replace("\"RecordId\": 1", "\"RecordId\": 2"))]);
        Assert.DoesNotContain(FlightManageForm.ChangedWhileOpenWarning, page.Renderer.Html);

        await page.LiveFeed.Page!.OnArrivalsAsync([Flight(Arrival.Replace("\"EstimatedTime\": \"2026-10-19T10:00:00\"", "\"EstimatedTime\": \"2026-10-19T10:20:00\""))]);
        var html = page.Renderer.Html;
        Assert.Contains(FlightManageForm.ChangedWhileOpenWarning, html);
        Assert.Matches("<input[^>]*class=\"[^\"]*fde-et[^\"]*\"[^>]*value=\"2026-10-19T10:20:00\"|<input[^>]*value=\"2026-10-19T10:20:00\"[^>]*class=\"[^\"]*fde-et", html);
        Assert.Empty(coreApi.ModifyBodies);

        await page.Renderer.ClickAsync("fde-save");
        Assert.Equal("""{"Preserved":true}""", Assert.Single(coreApi.ModifyBodies));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_Manage_save_shows_an_error_and_keeps_the_form_open(bool departures)
    {
        var coreApi = new FakeCoreApi { FeedStatus = "Feed stale", SaveStatus = HttpStatusCode.Conflict };
        await using var page = await OpenAsync(coreApi, Down);

        await ManageAsync(page, departures);
        await page.Renderer.ClickAsync("fde-save");

        var html = page.Renderer.Html;
        Assert.Contains("Could not save the flight: CoreAPI answered 409 Preserved edits cannot become Outage edits.", html);
        Assert.Contains("fde-save", html);
    }

    [Fact]
    public async Task A_failed_Add_Flight_shows_an_error_and_keeps_the_form_open()
    {
        var coreApi = new FakeCoreApi { SaveStatus = HttpStatusCode.InternalServerError };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-add");
        await page.Renderer.InputAsync("#flightCodeInput", "AC");
        await page.Renderer.ChangeAsync("#flightNumberInput", "555");
        await page.Renderer.ChangeAsync("#scheduledInput", "2026-10-19T12:00:00");
        await page.Renderer.ChangeAsync("#estimatedInput", "2026-10-19T12:00:00");
        await page.Renderer.ChangeAsync("#actualInput", "2026-10-19T12:00:00");
        await page.Renderer.ChangeAsync("#statusInput", "ON");
        await page.Renderer.InputAsync("#originInput", "YUL");
        await page.Renderer.ClickAsync("fde-save");

        Assert.Single(coreApi.AddBodies);
        var html = page.Renderer.Html;
        Assert.Contains("Could not save the flight: CoreAPI answered 500", html);
        Assert.Contains("fde-save", html);
    }

    private static FEArrivalFlight Flight(string json) =>
        JsonSerializer.Deserialize<FEArrivalFlight>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static async Task ManageAsync(OpenPage page, bool departures)
    {
        if (departures) await page.Renderer.ClickAsync("fde-departures");
        await page.Renderer.ClickAsync("fde-manage");
    }

    private static string[] OpenFields(string html, string[] fields) =>
        fields.Where(f => Regex.IsMatch(html, $"<(input|select)(?![^>]* disabled)[^>]*class=\"[^\"]*\\b{f}\\b[^\"]*\"[^>]*>")).ToArray();

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
        public ValueTask DisposeAsync()
        {
            Renderer.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Answers the routes the page loads and records each Flight Edit and Add Flight body.</summary>
    private sealed class FakeCoreApi : HttpMessageHandler
    {
        public string FeedStatus { get; init; } = "OK";

        /// <summary>The whole <c>FeedStatus</c> answer when not empty, in place of <see cref="FeedStatus"/>; null answers 500.</summary>
        public string? FeedStatusAnswer { get; init; } = "";

        public string Settings { get; init; } = "{}";

        public HttpStatusCode SaveStatus { get; init; } = HttpStatusCode.OK;

        public ConcurrentQueue<string> ModifyBodies { get; } = new();

        public ConcurrentQueue<string> AddBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var isSave = path.Contains("/Modify/") || path.EndsWith("/Add");
            if (path.Contains("/Modify/")) ModifyBodies.Enqueue(await request.Content!.ReadAsStringAsync(ct));
            if (path.EndsWith("/Add")) AddBodies.Enqueue(await request.Content!.ReadAsStringAsync(ct));
            if (path == "Override/FeedStatus" && FeedStatusAnswer is null)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("feed status failed") };
            if (path == "Override/FeedStatus" && FeedStatusAnswer is { Length: > 0 } answer)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
            if (isSave && SaveStatus != HttpStatusCode.OK)
                return new HttpResponseMessage(SaveStatus) { Content = new StringContent("Preserved edits cannot become Outage edits") };
            var json = path switch
            {
                "Override/Arrivals" => $"[{Arrival}]",
                "Override/Departures" => $"[{Departure}]",
                "Override/FeedStatus" => $$"""{ "Status": "{{FeedStatus}}" }""",
                "ApiKey/ApiKeyID" => "\"key-1\"",
                "Override/GetSettings/key-1" => Settings,
                "Override/Airlines" => """[ { "AirlineCode": "AC", "CarrierName": "Air Canada" }, { "AirlineCode": "LH", "CarrierName": "Lufthansa" }, { "AirlineCode": "DL", "CarrierName": "Delta" }, { "AirlineCode": "WS", "CarrierName": "WestJet" } ]""",
                "Override/Locations" => """[ { "IataCode": "YUL", "Name": "Montreal", "Country": "Canada", "LanguageCodes": "" }, { "IataCode": "YVR", "Name": "Vancouver", "Country": "Canada", "LanguageCodes": "" } ]""",
                "Override/Statuses" => """[ { "StatusId": "ON", "StatusName": "On time" }, { "StatusId": "DL", "StatusName": "Delayed" } ]""",
                "Override/Gates" => """[ { "Gat": "A1" }, { "Gat": "B2" } ]""",
                "Override/Carousels" => """[ { "CarouselId": "3", "BeltRelayIp": "" }, { "CarouselId": "4", "BeltRelayIp": "" } ]""",
                _ when path.Contains("/Modify/") => """{ "recordID": 1, "overrideResponse": "OK", "preserveOverride": true }""",
                _ when path.EndsWith("/Add") => "2",
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

    private sealed class StubFeedState(CoreApiFeedState? state) : ICoreApiFeedStateReader
    {
        public Task<CoreApiFeedState?> ReadAsync(CancellationToken ct) => Task.FromResult<CoreApiFeedState?>(state);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
