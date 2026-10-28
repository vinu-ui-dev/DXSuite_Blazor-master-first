using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Components.CoreApi;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Section numbers and clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-12 and AUD-05 (B-27): every Flight Data Editor action on the Flight Management page, driven over a fake
/// CoreAPI, writes its <c>fde.*</c> event (AUD-08 table) with the details a later reader needs to tell the whole story,
/// and a refused or failed attempt is an event with that outcome.
/// </summary>
public class FlightEditAuditTests
{
    private const string Server = "http://coreapi-audit.test";

    // 10:30 in Toronto (EDT): the window is 04:30 today to 04:30 tomorrow.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    private const string Arrival = """{ "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ORG": "YUL", "CAR": "3", "RCP": "ON", "ROP": "Y", "ROB": "Y", "ROS": "Y", "ROT": "Y", "ScheduledTime": "2026-10-19T10:00:00", "EstimatedTime": "2026-10-19T10:00:00", "CodeShares": [] }""";
    private const string Departure = """{ "RecordId": 7, "FLT": "WS700", "FLC": "WS", "FLN": "700", "DES": "YVR", "GAT": "A1", "RCP": "ON", "ROP": "Y", "ROB": "Y", "ROS": "Y", "ROT": "Y", "ScheduledTime": "2026-10-19T11:00:00", "EstimatedTime": "2026-10-19T11:00:00", "CodeShares": [] }""";

    private static readonly CoreApiFeedState Up = new("OK", true, null, Now.UtcDateTime);

    [Fact]
    public async Task A_Manage_save_logs_the_changed_field_with_its_old_and_new_value_and_the_kind()
    {
        var coreApi = new FakeCoreApi { Settings = """{ "ETModify": true }""" };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-manage");
        await page.Renderer.ChangeAsync("fde-et", "2026-10-19T10:45:00");
        await page.Renderer.ClickAsync("fde-save");

        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.flight-edited", written.ActionKey);
        Assert.Equal(new UserEventTarget("Flight", "arrival:1", "AC100 2026-10-19"), written.Target);
        Assert.Equal(UserEventOutcome.Succeeded, written.Outcome);
        Assert.Equal(
            """{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19","field":"estimated","old":"2026-10-19T10:00:00","new":"2026-10-19T10:45:00","kind":"preserved"}""",
            Json(written));
    }

    [Fact]
    public async Task A_roll_change_logs_the_categories_old_and_new_once()
    {
        var coreApi = new FakeCoreApi { Settings = """{ "ROSModify": true, "GATModify": true }""" };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-departures");
        await page.Renderer.ClickAsync("fde-manage");
        await page.Renderer.ChangeAsync("fde-staff", false);
        await page.Renderer.ChangeAsync("fde-gate", "B2");
        await page.Renderer.ClickAsync("fde-save");

        Assert.Equal(["fde.flight-edited", "fde.roll-changed"], page.Log.Written.Select(e => e.ActionKey));
        Assert.All(page.Log.Written, e => Assert.Equal(new UserEventTarget("Flight", "departure:7", "WS700 2026-10-19"), e.Target));
        Assert.Equal(
            """{"direction":"departure","recordId":7,"carrier":"WS","number":"700","scheduledDate":"2026-10-19","field":"gate","old":"A1","new":"B2","kind":"preserved"}""",
            Json(page.Log.Written.First()));
        Assert.Equal(
            """{"direction":"departure","recordId":7,"carrier":"WS","number":"700","scheduledDate":"2026-10-19","old":{"public":true,"baggage":true,"staff":true,"throw":true},"new":{"public":true,"baggage":true,"staff":false,"throw":true},"kind":"preserved"}""",
            Json(page.Log.Written.Last()));
    }

    [Fact]
    public async Task A_save_that_changes_no_field_logs_the_kind_it_saved()
    {
        var outageEdit = Arrival.Replace("\"CodeShares\": []", "\"CodeShares\": [], \"overrideFlightData\": { \"ETA\": \"2026-10-19T10:20:00\" }, \"preserveOverride\": false");
        var coreApi = new FakeCoreApi { Arrivals = $"[{outageEdit}]" };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-manage");
        await page.Renderer.ClickAsync("fde-save");

        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.flight-edited", written.ActionKey);
        Assert.Equal(
            """{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19","field":"kind","old":"outage","new":"preserved","kind":"preserved"}""",
            Json(written));
    }

    [Fact]
    public async Task Keeping_an_Outage_Edit_while_changing_a_field_logs_the_field_and_the_kind_change()
    {
        var coreApi = new FakeCoreApi { Arrivals = $"[{WithEdit(Arrival, preserved: false)}]", FeedStatus = "Feed stale" };
        await using var page = await OpenAsync(coreApi, new CoreApiFeedState("Feed stale", true, null, Now.UtcDateTime));

        await page.Renderer.ClickAsync("fde-manage");
        await page.Renderer.ChangeAsync("fde-keep-box", true);
        await page.Renderer.ChangeAsync("fde-status", "DL");
        await page.Renderer.ClickAsync("fde-save");

        Assert.Equal(
        [
            """{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19","field":"status","old":"ON","new":"DL","kind":"preserved"}""",
            """{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19","field":"kind","old":"outage","new":"preserved","kind":"preserved"}""",
        ], page.Log.Written.Select(Json));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, false, UserEventOutcome.Refused, "409 Preserved edits cannot become Outage edits")]
    [InlineData(HttpStatusCode.InternalServerError, false, UserEventOutcome.Failed, "500 Preserved edits cannot become Outage edits")]
    [InlineData(HttpStatusCode.OK, true, UserEventOutcome.Failed, "no CoreAPI server answered")]
    public async Task A_refused_or_failed_Manage_save_logs_the_attempt_with_that_outcome(
        HttpStatusCode status, bool unreachable, UserEventOutcome outcome, string error)
    {
        var coreApi = new FakeCoreApi { Settings = """{ "ETModify": true }""", SaveStatus = status, Unreachable = unreachable };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-manage");
        await page.Renderer.ChangeAsync("fde-et", "2026-10-19T10:45:00");
        await page.Renderer.ClickAsync("fde-save");

        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.flight-edited", written.ActionKey);
        Assert.Equal(outcome, written.Outcome);
        Assert.Contains("\"field\":\"estimated\"", Json(written));
        Assert.EndsWith($"\"error\":\"{error}\"}}", Json(written));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, UserEventOutcome.Succeeded)]
    [InlineData(HttpStatusCode.BadRequest, UserEventOutcome.Refused)]
    public async Task Add_Flight_logs_the_flight_key_every_field_set_the_categories_and_the_kind(HttpStatusCode status, UserEventOutcome outcome)
    {
        var coreApi = new FakeCoreApi { SaveStatus = status };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-add");
        await page.Renderer.InputAsync("#flightCodeInput", "AC");
        await page.Renderer.ChangeAsync("#flightNumberInput", "555");
        await page.Renderer.ChangeAsync("#scheduledInput", "2026-10-19T12:00:00");
        await page.Renderer.ChangeAsync("#estimatedInput", "2026-10-19T12:10:00");
        await page.Renderer.ChangeAsync("#actualInput", "2026-10-19T12:20:00");
        await page.Renderer.ChangeAsync("#statusInput", "ON");
        await page.Renderer.InputAsync("#originInput", "YUL");
        await page.Renderer.ClickAsync("fde-save");

        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.flight-added", written.ActionKey);
        Assert.Equal(outcome, written.Outcome);
        // CoreAPI gives the new flight's record id; a flight it did not add has none.
        var recordId = outcome == UserEventOutcome.Succeeded ? "2" : "null";
        Assert.Equal(new UserEventTarget("Flight", outcome == UserEventOutcome.Succeeded ? "arrival:2" : null, "AC555 2026-10-19"), written.Target);
        Assert.StartsWith(
            $$"""{"direction":"arrival","recordId":{{recordId}},"carrier":"AC","number":"555","scheduledDate":"2026-10-19","fields":{"scheduled":"2026-10-19T12:00:00","estimated":"2026-10-19T12:10:00","actual":"2026-10-19T12:20:00","status":"ON","origin":"YUL"},"categories":{"public":true,"baggage":true,"staff":true,"throw":true},"kind":"preserved""",
            Json(written));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, UserEventOutcome.Succeeded)]
    [InlineData(HttpStatusCode.ServiceUnavailable, UserEventOutcome.Failed)]
    public async Task Clearing_a_Preserved_Edit_from_Manage_logs_the_flight(HttpStatusCode status, UserEventOutcome outcome)
    {
        var coreApi = new FakeCoreApi { Arrivals = $"[{WithEdit(Arrival, preserved: true)}]", SaveStatus = status };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-manage");
        await page.Renderer.ClickAsync("fde-clear-edit");
        await page.Renderer.ClickAsync("fde-clear");

        Assert.Equal(["PUT Override/Arrivals/Clear/1"], coreApi.Calls);
        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.edit-cleared", written.ActionKey);
        Assert.Equal(outcome, written.Outcome);
        Assert.Equal(new UserEventTarget("Flight", "arrival:1", "AC100 2026-10-19"), written.Target);
        Assert.StartsWith("""{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19"}""".TrimEnd('}'), Json(written));
        if (outcome == UserEventOutcome.Succeeded)
            Assert.Equal("""{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19"}""", Json(written));
    }

    [Fact]
    public async Task Preserving_one_flight_in_the_review_logs_that_flight()
    {
        var coreApi = new FakeCoreApi { Departures = $"[{WithEdit(Departure, preserved: false)}]" };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-review");
        await page.Renderer.ClickAsync("fde-preserve");

        Assert.Equal(["PUT Override/Departures/Preserve/7"], coreApi.Calls);
        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.edit-preserved", written.ActionKey);
        Assert.Equal(new UserEventTarget("Flight", "departure:7", "WS700 2026-10-19"), written.Target);
        Assert.Equal("""{"direction":"departure","recordId":7,"carrier":"WS","number":"700","scheduledDate":"2026-10-19"}""", Json(written));
    }

    [Theory]
    [InlineData("fde-preserve-all", "PUT Override/PreserveAll", "fde.review-preserve-all", HttpStatusCode.OK, UserEventOutcome.Succeeded)]
    [InlineData("fde-use-feed", "PUT Override/ClearAll", "fde.review-use-feed", HttpStatusCode.OK, UserEventOutcome.Succeeded)]
    [InlineData("fde-use-feed", "PUT Override/ClearAll", "fde.review-use-feed", HttpStatusCode.InternalServerError, UserEventOutcome.Failed)]
    public async Task A_bulk_review_action_logs_the_window_and_every_affected_flight(
        string button, string call, string actionKey, HttpStatusCode status, UserEventOutcome outcome)
    {
        var coreApi = new FakeCoreApi
        {
            Arrivals = $"[{WithEdit(Arrival, preserved: false)}]",
            Departures = $"[{WithEdit(Departure, preserved: false)}]",
            Settings = """{ "PreserveAllOverridesOption": true }""",
            SaveStatus = status,
        };
        await using var page = await OpenAsync(coreApi, Up);

        await page.Renderer.ClickAsync("fde-review");
        await page.Renderer.ClickAsync(button);

        Assert.Equal([call], coreApi.Calls.Select(c => c.Split('?')[0]));
        var written = Assert.Single(page.Log.Written);
        Assert.Equal(actionKey, written.ActionKey);
        Assert.Equal(outcome, written.Outcome);
        Assert.Equal(new UserEventTarget("Flight list", null, "Flights 2026-10-19 04:30 to 2026-10-20 04:30"), written.Target);
        Assert.StartsWith(
            """{"windowStart":"2026-10-19T04:30:00-04:00","windowEnd":"2026-10-20T04:30:00-04:00","flights":[{"direction":"departure","recordId":7,"carrier":"WS","number":"700","scheduledDate":"2026-10-19"},{"direction":"arrival","recordId":1,"carrier":"AC","number":"100","scheduledDate":"2026-10-19"}]""",
            Json(written));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, UserEventOutcome.Succeeded, """{"on":true}""")]
    [InlineData(HttpStatusCode.BadGateway, UserEventOutcome.Failed, """{"on":true,"error":"502 Preserved edits cannot become Outage edits"}""")]
    public async Task A_user_turning_the_Outage_mode_switch_logs_an_Account_event(HttpStatusCode status, UserEventOutcome outcome, string details)
    {
        var coreApi = new FakeCoreApi { SaveStatus = status };
        await using var page = await OpenAsync(coreApi, Up, allowSwitch: true);

        await page.Renderer.ChangeAsync("fde-outage-switch", true);

        var written = Assert.Single(page.Log.Written);
        Assert.Equal("fde.outage-switched", written.ActionKey);
        Assert.Equal(outcome, written.Outcome);
        Assert.Equal(new UserEventTarget("Flight feed", null, "Flight feed"), written.Target);
        Assert.Equal(details, Json(written));
    }

    [Fact]
    public async Task Saving_the_field_settings_logs_one_event_per_setting_changed_with_old_and_new()
    {
        var coreApi = new FakeCoreApi { Settings = """{ "GATModify": false, "ETModify": true }""" };
        await using var settings = await OpenSettingsAsync(coreApi);

        await settings.Renderer.ChangeAsync("fde-setting-GATModify", true);
        await settings.Renderer.ChangeAsync("fde-setting-ETModify", false);
        await settings.Renderer.ClickAsync("fde-settings-save");
        // A save that changes nothing logs nothing more.
        await settings.Renderer.ClickAsync("fde-settings-save");

        Assert.Equal(2, coreApi.Calls.Count);
        Assert.All(settings.Log.Written, e =>
        {
            Assert.Equal("fde.settings-changed", e.ActionKey);
            Assert.Equal(UserEventOutcome.Succeeded, e.Outcome);
            Assert.Equal(new UserEventTarget("CoreAPI settings", null, "Flight Data Editor field settings"), e.Target);
        });
        Assert.Equal(
            ["""{"setting":"GATModify","old":false,"new":true}""", """{"setting":"ETModify","old":true,"new":false}"""],
            settings.Log.Written.Select(Json));
    }

    [Fact]
    public async Task A_refused_field_settings_save_logs_each_attempted_change_as_Refused()
    {
        var coreApi = new FakeCoreApi { SaveStatus = HttpStatusCode.Forbidden };
        await using var settings = await OpenSettingsAsync(coreApi);

        await settings.Renderer.ChangeAsync("fde-setting-PreserveAllOverridesOption", true);
        await settings.Renderer.ClickAsync("fde-settings-save");

        var written = Assert.Single(settings.Log.Written);
        Assert.Equal(UserEventOutcome.Refused, written.Outcome);
        Assert.Equal("""{"setting":"PreserveAllOverridesOption","old":false,"new":true,"error":"403 Preserved edits cannot become Outage edits"}""", Json(written));
    }

    // The flight with a Flight Edit on its estimated time, of the kind given.
    private static string WithEdit(string flight, bool preserved) => flight.Replace("\"CodeShares\": []",
        $"\"CodeShares\": [], \"overrideFlightData\": {{ \"{(flight == Arrival ? "ETA" : "ETD")}\": \"2026-10-19T12:00:00\" }}, \"preserveOverride\": {(preserved ? "true" : "false")}");

    private static string Json(UserEvent written) => JsonSerializer.Serialize(written.Details ?? new { }, JsonSerializerOptions.Web);

    private static async Task<OpenPage> OpenAsync(FakeCoreApi coreApi, CoreApiFeedState state, bool allowSwitch = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FlightListClock.AirportTimeZoneKey] = "America/Toronto" })
            .Build();
        var log = new RecordingLog();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(new KeySession(PermissionKeys.PageFlightDataEditor));
        services.AddSingleton<NavigationManager, TestNavigationManager>();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICoreApiSettingsStore>(new CoreApiClientTests.FakeSettingsStore(Server));
        services.AddSingleton<ICoreApiLiveFeed, NoLiveFeed>();
        services.AddSingleton<ICoreApiFeedStateReader>(new StubFeedState(state));
        services.AddSingleton<IFlightListClock>(new FlightListClock(config, new FixedTime(Now)));
        services.AddSingleton(Options.Create(new FlightDataEditorOptions { AllowOutageModeSwitch = allowSwitch }));
        services.AddSingleton<IUserEventLog>(log);
        services.AddFlightEditAudit();
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => coreApi);

        var renderer = await InteractiveRenderer.RenderAsync<FlightManagement>(new Dictionary<string, object?>(), services.BuildServiceProvider());
        return new OpenPage(renderer, log);
    }

    private static async Task<OpenPage> OpenSettingsAsync(FakeCoreApi coreApi)
    {
        var log = new RecordingLog();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(new KeySession(PermissionKeys.ConfigTabCoreApiConnection));
        services.AddSingleton<ICoreApiSettingsStore>(new CoreApiClientTests.FakeSettingsStore(Server));
        services.AddSingleton<IUserEventLog>(log);
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => coreApi);

        var renderer = await InteractiveRenderer.RenderAsync<FlightDataEditorUserSettings>(new Dictionary<string, object?>(), services.BuildServiceProvider());
        return new OpenPage(renderer, log);
    }

    private sealed record OpenPage(InteractiveRenderer Renderer, RecordingLog Log) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Renderer.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records the Account events the signed-in user's actions write.</summary>
    internal sealed class RecordingLog : IUserEventLog
    {
        public ConcurrentQueue<UserEvent> Written { get; } = new();

        public Task WriteAsync(UserEvent userEvent, CancellationToken ct = default)
        {
            Written.Enqueue(userEvent);
            return Task.CompletedTask;
        }

        public Task WriteAsync(UserEventActor actor, UserEvent userEvent, CancellationToken ct = default) =>
            throw new NotSupportedException("Flight Data Editor events are the signed-in account's.");

        public Task<UserEventActor> CurrentAccountAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<UserEventActor> AccountAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Answers the routes the page loads; saves and review calls answer <see cref="SaveStatus"/>.</summary>
    private sealed class FakeCoreApi : HttpMessageHandler
    {
        public string Arrivals { get; init; } = $"[{Arrival}]";

        public string Departures { get; init; } = $"[{Departure}]";

        public string FeedStatus { get; init; } = "OK";

        public string Settings { get; init; } = "{}";

        public HttpStatusCode SaveStatus { get; init; } = HttpStatusCode.OK;

        public bool Unreachable { get; init; }

        public ConcurrentQueue<string> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var isAction = request.Method != HttpMethod.Get;
            if (isAction)
            {
                Calls.Enqueue($"{request.Method} {path}");
                if (Unreachable) throw new HttpRequestException("No route to host");
                if (SaveStatus != HttpStatusCode.OK)
                    return Task.FromResult(new HttpResponseMessage(SaveStatus) { Content = new StringContent("Preserved edits cannot become Outage edits") });
            }
            var json = path switch
            {
                "Override/Arrivals" => Arrivals,
                "Override/Departures" => Departures,
                "Override/FeedStatus" => $$"""{ "Status": "{{FeedStatus}}" }""",
                "ApiKey/ApiKeyID" => "\"key-1\"",
                "Override/GetSettings/key-1" => Settings,
                "Override/Airlines" => """[ { "AirlineCode": "AC", "CarrierName": "Air Canada" }, { "AirlineCode": "WS", "CarrierName": "WestJet" } ]""",
                "Override/Locations" => """[ { "IataCode": "YUL", "Name": "Montreal", "Country": "Canada", "LanguageCodes": "" }, { "IataCode": "YVR", "Name": "Vancouver", "Country": "Canada", "LanguageCodes": "" } ]""",
                "Override/Statuses" => """[ { "StatusId": "ON", "StatusName": "On time" }, { "StatusId": "DL", "StatusName": "Delayed" } ]""",
                "Override/Gates" => """[ { "Gat": "A1" }, { "Gat": "B2" } ]""",
                "Override/Carousels" => """[ { "CarouselId": "3", "BeltRelayIp": "" }, { "CarouselId": "4", "BeltRelayIp": "" } ]""",
                "Override/OverrideToggle" => """{ "feedOverride": true }""",
                _ when path.EndsWith("/Add") => "2",
                _ when path.Contains("/Modify/") || path.Contains("/Preserve/") || path.Contains("/Clear/") =>
                    """{ "recordID": 1, "overrideResponse": "OK", "preserveOverride": true }""",
                _ when isAction => "\"OK\"",
                _ => "[]",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class NoLiveFeed : ICoreApiLiveFeed
    {
        public Task<IAsyncDisposable> SubscribeAsync(ICoreApiHubObserver page, CancellationToken ct = default) =>
            Task.FromResult<IAsyncDisposable>(new Nothing());

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
