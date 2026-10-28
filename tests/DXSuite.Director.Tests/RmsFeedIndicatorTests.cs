using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-02 and CAPI-04 (B-21): the Flight Data Editor's "RMS Feed" indicator follows the feed state the CoreAPI
/// watcher keeps (B-11). <c>FeedStatus.Status</c> alone says whether the feed is up; <c>ArrivalsLast</c> and
/// <c>DeparturesLast</c> are information only and may be missing.
/// </summary>
public class RmsFeedIndicatorTests
{
    private static readonly DateTime Changed = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_indicator_shows_the_watchers_state_not_the_pages_own_answer()
    {
        var rendered = await RenderAsync(new CoreApiFeedState("Arrivals feed stale", true, null, Changed),
            new FeedStatus { Status = "OK", ArrivalsStatus = "OK", DeparturesStatus = "OK" });

        Assert.Contains("RMS feed down", rendered);
        Assert.Contains("Overall Feed Status: Arrivals feed stale", rendered);
    }

    [Fact]
    public async Task An_OK_status_shows_the_feed_up()
    {
        var rendered = await RenderAsync(new CoreApiFeedState("OK", true, null, Changed), details: null);

        Assert.Contains("RMS feed up", rendered);
        Assert.Contains("Overall Feed Status: OK", rendered);
    }

    // The null dereference this replaces threw while rendering whenever CoreAPI left a last sync time out.
    [Fact]
    public async Task Missing_last_sync_times_render_blank_without_an_error()
    {
        var rendered = await RenderAsync(new CoreApiFeedState("OK", true, null, Changed),
            new FeedStatus { Status = "OK", ArrivalsStatus = "Running", ArrivalsLast = null, DeparturesStatus = "Running", DeparturesLast = null });

        Assert.Contains("Arrival Feed Status: Running", rendered);
        Assert.Contains("Arrival Feed Last Sync: ", rendered);
        Assert.Contains("Departure Feed Last Sync: ", rendered);
    }

    [Fact]
    public void A_last_sync_time_CoreAPI_gives_is_shown()
    {
        var reading = RmsFeedIndicator.Describe(new CoreApiFeedState("OK", true, null, Changed),
            new FeedStatus { ArrivalsLast = new DateTime(2026, 10, 19, 9, 5, 0), DeparturesLast = null });

        Assert.NotEqual("", reading.Rows.Single(r => r.Label == "Arrival Feed Last Sync:").Value);
        Assert.Equal("", reading.Rows.Single(r => r.Label == "Departure Feed Last Sync:").Value);
    }

    [Fact]
    public async Task An_unreachable_CoreAPI_shows_as_unreachable_whatever_the_last_status()
    {
        var rendered = await RenderAsync(new CoreApiFeedState("OK", false, null, Changed), details: null);

        Assert.Contains("CoreAPI unreachable", rendered);
        Assert.DoesNotContain("RMS feed up", rendered);
    }

    // No row yet: the watcher has not reported, so the indicator claims neither up nor down.
    [Fact]
    public async Task Before_the_watcher_reports_the_state_shows_as_not_known()
    {
        var rendered = await RenderAsync(state: null, new FeedStatus { Status = "OK" });

        Assert.Contains("RMS feed state not known yet", rendered);
        Assert.Equal(RmsFeedIndicator.Light.Unknown, RmsFeedIndicator.Describe(null, new FeedStatus { Status = "OK" }).Light);
    }

    [Fact]
    public async Task The_page_is_told_the_state_the_indicator_read()
    {
        var state = new CoreApiFeedState("DOWN", true, null, Changed);
        CoreApiFeedState? told = null;
        var services = Services(state);

        await PageRenderer.RenderAsync(services, typeof(RmsFeedIndicator), new Dictionary<string, object?>
        {
            [nameof(RmsFeedIndicator.StateChanged)] = Microsoft.AspNetCore.Components.EventCallback.Factory
                .Create<CoreApiFeedState?>(new object(), s => told = s),
        });

        Assert.Equal(state, told);
    }

    /// <summary>The indicator's text with whitespace collapsed and the scoped-CSS markers taken out.</summary>
    private static async Task<string> RenderAsync(CoreApiFeedState? state, FeedStatus? details)
    {
        var rendered = await PageRenderer.RenderAsync(Services(state), typeof(RmsFeedIndicator), new Dictionary<string, object?>
        {
            [nameof(RmsFeedIndicator.Details)] = details,
        });
        var text = System.Text.RegularExpressions.Regex.Replace(rendered, @"\[b-[a-z0-9]+\]", "");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private static IServiceProvider Services(CoreApiFeedState? state) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<ICoreApiFeedStateReader>(new StubReader(state))
            .BuildServiceProvider();

    private sealed class StubReader(CoreApiFeedState? state) : ICoreApiFeedStateReader
    {
        public Task<CoreApiFeedState?> ReadAsync(CancellationToken ct) => Task.FromResult(state);
    }
}
