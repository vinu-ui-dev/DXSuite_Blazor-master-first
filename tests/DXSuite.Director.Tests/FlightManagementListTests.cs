using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-10 and FDE-02 (B-21) on the Flight Management page itself, over a fake CoreAPI: the list opens on Arrivals,
/// asks CoreAPI for the window in airport time, shows only flights whose best-known time is in it, and the "RMS Feed"
/// indicator shows the watcher's state.
/// </summary>
public class FlightManagementListTests
{
    private const string Server = "http://coreapi.test";

    // 10:30 in Toronto (EDT): the window is 04:30 today to 04:30 tomorrow.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    private const string Arrivals = """
        [
          { "RecordId": 1, "FLT": "AC100", "FLC": "AC", "FLN": "100", "ScheduledTime": "2026-10-19T10:00:00", "CodeShares": [] },
          { "RecordId": 2, "FLT": "AC200", "FLC": "AC", "FLN": "200", "ScheduledTime": "2026-10-19T08:00:00", "ActualTime": "2026-10-19T04:29:59", "CodeShares": [] },
          { "RecordId": 3, "FLT": "AC300", "FLC": "AC", "FLN": "300", "ScheduledTime": "2026-10-20T04:29:59", "CodeShares": [] },
          { "RecordId": 4, "FLT": "AC400", "FLC": "AC", "FLN": "400", "ScheduledTime": "2026-10-20T05:00:00", "CodeShares": [] }
        ]
        """;

    private const string Departures = """
        [ { "RecordId": 9, "FLT": "WS700", "FLC": "WS", "FLN": "700", "ScheduledTime": "2026-10-19T11:00:00", "CodeShares": [] } ]
        """;

    [Fact]
    public async Task The_list_opens_on_Arrivals()
    {
        var rendered = await RenderAsync(new FakeCoreApi());

        Assert.Contains("ARRIVING TIME", rendered);
        Assert.DoesNotContain("DEPARTING TIME", rendered);
        Assert.Contains("Departures", rendered);
        Assert.DoesNotContain("WS700", rendered);
    }

    [Fact]
    public async Task The_page_asks_CoreAPI_for_the_window_in_airport_time_as_ISO_8601()
    {
        var coreApi = new FakeCoreApi();

        await RenderAsync(coreApi);

        foreach (var route in new[] { "Override/Arrivals", "Override/Departures" })
        {
            var query = QueryHelpers.ParseQuery(Assert.Single(coreApi.Requests, r => r.AbsolutePath.TrimStart('/') == route).Query);
            Assert.Equal("2026-10-19T04:30:00-04:00", query["StartDateTime"].ToString());
            Assert.Equal("2026-10-20T04:30:00-04:00", query["EndDateTime"].ToString());
        }
    }

    [Fact]
    public async Task Only_flights_whose_best_known_time_is_in_the_window_are_listed()
    {
        var rendered = await RenderAsync(new FakeCoreApi());

        Assert.Contains("AC100", rendered);
        Assert.Contains("AC300", rendered);
        // Scheduled inside the window, but it landed a second before the window starts.
        Assert.DoesNotContain("AC200", rendered);
    }

    [Fact]
    public async Task The_window_moves_forward_with_the_clock_and_the_lists_are_read_again_for_it()
    {
        var coreApi = new FakeCoreApi();
        var clock = new TwoHost.ManualClock(Now);
        var liveFeed = new CapturingLiveFeed();
        using var page = await RenderInteractiveAsync(coreApi, clock, liveFeed);
        // 05:00 tomorrow is past the window's end.
        Assert.DoesNotContain("AC400", page.Html);

        clock.Advance(TimeSpan.FromHours(1));

        await WaitForAsync(() => page.Html.Contains("AC400"));
        foreach (var route in new[] { "Override/Arrivals", "Override/Departures" })
        {
            var query = QueryHelpers.ParseQuery(coreApi.Requests.Last(r => r.AbsolutePath.TrimStart('/') == route).Query);
            Assert.Equal("2026-10-19T05:30:00-04:00", query["StartDateTime"].ToString());
            Assert.Equal("2026-10-20T05:30:00-04:00", query["EndDateTime"].ToString());
        }

        // A hub update is kept or dropped by the window as it is now, not as it was when the page opened.
        await liveFeed.Page!.OnArrivalsAsync([new() { RecordId = 5, Type = "A", FLT = "AC500", FLC = "AC", FLN = "500", ScheduledTime = new DateTime(2026, 10, 20, 5, 15, 0) }]);
        Assert.Contains("AC500", page.Html);
    }

    [Fact]
    public async Task The_window_follows_the_clock_at_least_every_minute()
    {
        Assert.True(FlightManagement.WindowRefresh <= TimeSpan.FromMinutes(1));

        var coreApi = new FakeCoreApi();
        var clock = new TwoHost.ManualClock(Now);
        using var page = await RenderInteractiveAsync(coreApi, clock, new CapturingLiveFeed());
        var reads = coreApi.Requests.Count(r => r.AbsolutePath.TrimStart('/') == "Override/Arrivals");

        clock.Advance(FlightManagement.WindowRefresh);

        await WaitForAsync(() => coreApi.Requests.Count(r => r.AbsolutePath.TrimStart('/') == "Override/Arrivals") == reads + 1);
    }

    [Fact]
    public async Task Each_list_is_in_order_of_best_known_time()
    {
        // Best known: AC120 lands at 10:00 (actual), AC110 at 11:00 (scheduled), AC130 at 12:00 (estimated).
        var coreApi = new FakeCoreApi
        {
            ArrivalsAnswer = """
                [
                  { "RecordId": 11, "FLT": "AC130", "FLC": "AC", "FLN": "130", "ScheduledTime": "2026-10-19T09:00:00", "EstimatedTime": "2026-10-19T12:00:00", "CodeShares": [] },
                  { "RecordId": 12, "FLT": "AC110", "FLC": "AC", "FLN": "110", "ScheduledTime": "2026-10-19T11:00:00", "CodeShares": [] },
                  { "RecordId": 13, "FLT": "AC120", "FLC": "AC", "FLN": "120", "ScheduledTime": "2026-10-19T13:00:00", "ActualTime": "2026-10-19T10:00:00", "CodeShares": [] }
                ]
                """,
            DeparturesAnswer = """
                [
                  { "RecordId": 21, "FLT": "WS730", "FLC": "WS", "FLN": "730", "ScheduledTime": "2026-10-19T09:00:00", "EstimatedTime": "2026-10-19T12:00:00", "CodeShares": [] },
                  { "RecordId": 22, "FLT": "WS710", "FLC": "WS", "FLN": "710", "ScheduledTime": "2026-10-19T11:00:00", "CodeShares": [] },
                  { "RecordId": 23, "FLT": "WS720", "FLC": "WS", "FLN": "720", "ScheduledTime": "2026-10-19T13:00:00", "ActualTime": "2026-10-19T10:00:00", "CodeShares": [] }
                ]
                """,
        };
        using var page = await RenderInteractiveAsync(coreApi, new TwoHost.ManualClock(Now), new CapturingLiveFeed());

        AssertInOrder(page.Html, "AC120", "AC110", "AC130");

        await page.ClickAsync("fde-departures");
        AssertInOrder(page.Html, "WS720", "WS710", "WS730");
    }

    private static void AssertInOrder(string html, params string[] flights)
    {
        var positions = flights.Select(f => html.IndexOf(f, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order().ToList(), positions);
    }

    [Fact]
    public async Task The_indicator_follows_the_watchers_state_and_a_missing_last_sync_is_no_error()
    {
        var coreApi = new FakeCoreApi { FeedStatus = """{ "Status": "OK", "ArrivalsStatus": "OK", "ArrivalsLast": null, "DeparturesLast": null }""" };

        var rendered = await RenderAsync(coreApi, new CoreApiFeedState("Feed stale", true, null, Now.UtcDateTime));

        Assert.Contains("RMS feed down", rendered);
        Assert.DoesNotContain("RMS feed up", rendered);
    }

    [Fact]
    public async Task An_instance_without_the_airport_time_zone_setting_says_so()
    {
        var rendered = await RenderAsync(new FakeCoreApi(), airportTimeZone: null);

        Assert.Contains(FlightListClock.AirportTimeZoneKey + " is not set", rendered);
    }

    private static async Task<string> RenderAsync(FakeCoreApi coreApi, CoreApiFeedState? feedState = null, string? airportTimeZone = "America/Toronto")
    {
        var settings = new Dictionary<string, string?>();
        if (airportTimeZone is not null) settings[FlightListClock.AirportTimeZoneKey] = airportTimeZone;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var session = new KeySession(PermissionKeys.PageFlightDataEditor);

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(session);
        services.AddSingleton<NavigationManager, TestNavigationManager>();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICoreApiSettingsStore>(new CoreApiClientTests.FakeSettingsStore(Server));
        services.AddSingleton<ICoreApiLiveFeed, NoLiveFeed>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new FlightDataEditorOptions()));
        services.AddFlightEditAudit();
        services.AddSingleton<ICoreApiFeedStateReader>(new StubFeedState(feedState ?? new CoreApiFeedState("OK", true, null, Now.UtcDateTime)));
        services.AddSingleton<IFlightListClock>(new FlightListClock(config, new FixedTime(Now)));
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => coreApi);

        var rendered = await PageRenderer.RenderAsync(services.BuildServiceProvider(), typeof(FlightManagement));
        var text = System.Text.RegularExpressions.Regex.Replace(rendered, @"\[b-[a-z0-9]+\]", "");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private static Task<InteractiveRenderer> RenderInteractiveAsync(FakeCoreApi coreApi, TimeProvider clock, ICoreApiLiveFeed liveFeed)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FlightListClock.AirportTimeZoneKey] = "America/Toronto" })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISessionState>(new KeySession(PermissionKeys.PageFlightDataEditor));
        services.AddSingleton<NavigationManager, TestNavigationManager>();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICoreApiSettingsStore>(new CoreApiClientTests.FakeSettingsStore(Server));
        services.AddSingleton(liveFeed);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new FlightDataEditorOptions()));
        services.AddFlightEditAudit();
        services.AddSingleton<ICoreApiFeedStateReader>(new StubFeedState(new CoreApiFeedState("OK", true, null, Now.UtcDateTime)));
        services.AddSingleton<IFlightListClock>(new FlightListClock(config, clock));
        services.AddCoreApiClient().ConfigurePrimaryHttpMessageHandler(() => coreApi);
        return InteractiveRenderer.RenderAsync<FlightManagement>(new Dictionary<string, object?>(), services.BuildServiceProvider());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The page did not reach the expected state in time.");
            await Task.Delay(20);
        }
    }

    private sealed class CapturingLiveFeed : ICoreApiLiveFeed
    {
        public ICoreApiHubObserver? Page { get; private set; }

        public Task<IAsyncDisposable> SubscribeAsync(ICoreApiHubObserver page, CancellationToken ct = default)
        {
            Page = page;
            return Task.FromResult<IAsyncDisposable>(new NoLiveFeed.Nothing());
        }
    }

    /// <summary>Answers each CoreAPI route the page loads; anything else gets an empty list.</summary>
    private sealed class FakeCoreApi : HttpMessageHandler
    {
        public string FeedStatus { get; init; } = """{ "Status": "OK" }""";

        public string ArrivalsAnswer { get; init; } = Arrivals;

        public string DeparturesAnswer { get; init; } = Departures;

        public ConcurrentQueue<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue(request.RequestUri!);
            var json = request.RequestUri!.AbsolutePath.TrimStart('/') switch
            {
                "Override/Arrivals" => ArrivalsAnswer,
                "Override/Departures" => DeparturesAnswer,
                "Override/FeedStatus" => FeedStatus,
                "ApiKey/ApiKeyID" => "\"key-1\"",
                _ => "[]",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class NoLiveFeed : ICoreApiLiveFeed
    {
        public Task<IAsyncDisposable> SubscribeAsync(ICoreApiHubObserver page, CancellationToken ct = default) =>
            Task.FromResult<IAsyncDisposable>(new Nothing());

        internal sealed class Nothing : IAsyncDisposable
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
