using System.Text.RegularExpressions;
using DXSuite.Aviation.Pages.FlightDataEditor;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-26 (FDE-09): the Flight Data Editor list marks a flight a user rolled off some Display Categories with a blue
/// eye-slash and the number of categories, beside the Outage Edit triangle or Preserved Edit padlock; clicking it lists
/// each category as Shown or Hidden. The legend under the list names all three icons.
/// </summary>
public class FlightListHiddenCueTests
{
    private const string On = "Y";

    // The feed's unset Public and Baggage flags add nothing: only the edit's flags count.
    [Fact]
    public void A_departure_with_Staff_and_Throw_rolled_off_is_hidden_from_two_categories()
    {
        var flight = Departure(edit: new OverrideDepartureFlight { ROS = "N", ROT = "N" }, rop: null, rob: "");

        var cue = FlightListCue.For(flight);

        Assert.Equal(2, cue.HiddenCount);
        Assert.Equal([DisplayCategory.Staff, DisplayCategory.Throw], cue.HiddenFrom);
        Assert.False(cue.HiddenEverywhere);
    }

    [Fact]
    public void An_arrival_rolled_off_all_four_categories_is_hidden_everywhere()
    {
        var flight = Arrival(edit: new OverrideArrivalFlight { ROP = "N", ROB = "N", ROS = "N", ROT = "N" }, preserved: true);

        var cue = FlightListCue.For(flight);

        Assert.Equal(4, cue.HiddenCount);
        Assert.True(cue.HiddenEverywhere);
    }

    // CoreAPI's roll-off time rule takes a flight off screens without touching its flags, so a departed flight
    // nobody rolled off carries no cue (DXME-453: only user roll-offs count, no new CoreAPI ask).
    [Fact]
    public void A_flight_only_CoreAPIs_timing_took_off_screens_has_no_cue()
    {
        var flight = Departure();
        flight.ScheduledTime = DateTime.Now.AddHours(-5);
        flight.ActualTime = DateTime.Now.AddHours(-5);
        flight.RCP = "Departed";

        var cue = FlightListCue.For(flight);

        Assert.Equal(0, cue.HiddenCount);
        Assert.Empty(cue.HiddenFrom);
        Assert.Equal(FlightEditKind.None, cue.Edit);
    }

    // Hidden means the edit's flag is not on, whatever CoreAPI's explicit off becomes (C-01).
    [Theory]
    [InlineData("N")]
    [InlineData("n")]
    [InlineData("0")]
    [InlineData("false")]
    public void Any_edit_flag_other_than_on_counts_as_hidden(string off)
    {
        var cue = FlightListCue.For(Arrival(edit: new OverrideArrivalFlight { ROP = off, ROB = off }));

        Assert.Equal([DisplayCategory.Public, DisplayCategory.Baggage], cue.HiddenFrom);
    }

    // Only categories a user rolled off count (FDE-09), so a feed flag, set to off or not set at all, gives no cue.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N")]
    public void A_flight_with_no_edit_has_no_cue_whatever_its_feed_flags(string? feed)
    {
        Assert.Empty(FlightListCue.For(Departure(rop: feed, rob: feed, ros: feed, rot: feed)).HiddenFrom);
        Assert.Empty(FlightListCue.For(Arrival(rop: feed, rob: feed, ros: feed, rot: feed)).HiddenFrom);
    }

    // An edit that changed other fields leaves its roll flags unset; those are no user roll-off.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void An_edit_flag_left_unset_is_no_roll_off(string? unset)
    {
        var cue = FlightListCue.For(Departure(
            edit: new OverrideDepartureFlight { ETD = DateTime.Now, ROP = unset, ROB = unset, ROS = unset, ROT = unset },
            rop: null, rob: null, ros: null, rot: null));

        Assert.Equal(0, cue.HiddenCount);
        Assert.Equal(FlightEditKind.OutageEdit, cue.Edit);
    }

    [Fact]
    public void An_edit_that_rolls_a_category_back_on_shows_it()
    {
        var cue = FlightListCue.For(Departure(edit: new OverrideDepartureFlight { ROP = "Y" }, rop: "N"));

        Assert.Equal(0, cue.HiddenCount);
    }

    [Theory]
    [InlineData(false, false, FlightEditKind.None)]
    [InlineData(true, false, FlightEditKind.OutageEdit)]
    [InlineData(true, true, FlightEditKind.PreservedEdit)]
    public void The_first_slot_shows_the_edit_kind_as_built(bool hasEdit, bool preserved, FlightEditKind expected)
    {
        var departure = Departure(edit: hasEdit ? new OverrideDepartureFlight { ETD = DateTime.Now } : null, preserved: preserved);
        var arrival = Arrival(edit: hasEdit ? new OverrideArrivalFlight { ETA = DateTime.Now } : null, preserved: preserved);

        Assert.Equal(expected, FlightListCue.For(departure).Edit);
        Assert.Equal(expected, FlightListCue.For(arrival).Edit);
    }

    // ---- The two icon slots on a row ----

    [Fact]
    public async Task The_eye_slash_carries_the_number_of_hidden_categories()
    {
        var row = await Icons(Departure(edit: new OverrideDepartureFlight { ROS = "N", ROT = "N" }));

        Assert.Contains("bi-eye-slash-fill", row.Html);
        Assert.Equal("2", Text(Single(row.Html, "fde-cue-count")));
        Assert.DoesNotContain("fde-cue-panel", row.Html);
    }

    [Fact]
    public async Task Clicking_the_eye_slash_lists_each_category_as_shown_or_hidden()
    {
        var row = await Icons(Departure(edit: new OverrideDepartureFlight { ROS = "N", ROT = "N" }));

        await row.ClickAsync("fde-cue-btn");

        Assert.Equal(["Public Shown", "Baggage Shown", "Staff Hidden", "Throw Hidden"], PanelRows(row.Html));
        Assert.Contains("Change this in Manage, under Roll On / Off.", Text(row.Html));
        Assert.DoesNotContain("every Display Category", Text(row.Html));
    }

    [Fact]
    public async Task A_flight_hidden_from_all_four_shows_4_and_its_panel_says_so()
    {
        var row = await Icons(Arrival(edit: new OverrideArrivalFlight { ROP = "N", ROB = "N", ROS = "N", ROT = "N" }));

        Assert.Equal("4", Text(Single(row.Html, "fde-cue-count")));
        await row.ClickAsync("fde-cue-btn");

        Assert.Equal(["Public Hidden", "Baggage Hidden", "Staff Hidden", "Throw Hidden"], PanelRows(row.Html));
        Assert.Contains("Hidden from every Display Category.", Text(row.Html));
        Assert.Contains("Change this in Manage, under Roll On / Off.", Text(row.Html));
    }

    [Fact]
    public async Task The_panel_closes_on_a_click_outside_it_or_on_the_eye_slash_again()
    {
        var row = await Icons(Departure(edit: new OverrideDepartureFlight { ROP = "N" }));

        await row.ClickAsync("fde-cue-btn");
        await row.ClickAsync("fde-cue-backdrop");
        Assert.DoesNotContain("fde-cue-panel", row.Html);

        await row.ClickAsync("fde-cue-btn");
        await row.ClickAsync("fde-cue-btn");
        Assert.DoesNotContain("fde-cue-panel", row.Html);
    }

    // The slots are fixed so the icons line up row to row: an empty slot still takes its place.
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, "bi-exclamation-triangle-fill")]
    [InlineData(true, true, "bi-lock-fill")]
    public async Task Every_row_has_two_fixed_slots_the_edit_icon_first(bool hasEdit, bool preserved, string? editIcon)
    {
        var row = await Icons(Departure(edit: hasEdit ? new OverrideDepartureFlight { ETD = DateTime.Now } : null, preserved: preserved));

        var slots = Regex.Matches(row.Html, "<span class=\"fde-slot[^\"]*\"[^>]*>").Count;
        Assert.Equal(2, slots);
        Assert.DoesNotContain("bi-eye-slash-fill", row.Html);
        foreach (var icon in new[] { "bi-exclamation-triangle-fill", "bi-lock-fill" })
            Assert.Equal(icon == editIcon, row.Html.Contains(icon));
    }

    [Fact]
    public async Task The_eye_slash_sits_in_the_second_slot_beside_the_padlock()
    {
        var row = await Icons(Departure(edit: new OverrideDepartureFlight { ROB = "N" }, preserved: true));

        var html = row.Html;
        Assert.True(html.IndexOf("bi-lock-fill", StringComparison.Ordinal) < html.IndexOf("bi-eye-slash-fill", StringComparison.Ordinal));
        Assert.Equal("1", Text(Single(html, "fde-cue-count")));
    }

    // The padlock keeps its built behaviour: clicking it opens the clear-or-keep review for that flight.
    [Fact]
    public async Task Clicking_the_padlock_still_opens_the_flights_review()
    {
        var clicked = 0;
        var row = await Icons(Departure(edit: new OverrideDepartureFlight { ETD = DateTime.Now }, preserved: true),
            onPadlock: () => clicked++);

        await row.ClickAsync("bi-lock-fill");

        Assert.Equal(1, clicked);
    }

    // ---- The legend under the list (Diagram 9) ----

    [Fact]
    public async Task The_legend_reads_Outage_Edit_Preserved_Edit_and_Hidden_from_some_displays()
    {
        var legend = await InteractiveRenderer.RenderAsync<FlightListLegend>(new Dictionary<string, object?>());

        var entries = Regex.Matches(legend.Html, "<span class=\"fde-legend-entry\"[^>]*>(.*?)</span>")
            .Select(m => (Icon: Regex.Match(m.Value, "bi-[a-z-]+").Value, Label: Text(m.Groups[1].Value)))
            .ToArray();
        Assert.Equal(
            [("bi-exclamation-triangle-fill", "Outage Edit"), ("bi-lock-fill", "Preserved Edit"), ("bi-eye-slash-fill", "Hidden from some displays")],
            entries);
    }

    // ---- The list ----

    [Fact]
    public void Both_lists_draw_their_rows_icons_with_the_two_slot_component_and_show_the_legend()
    {
        var page = Source("FlightManagement.razor");

        Assert.Equal(2, Regex.Matches(page, "<FlightRowIcons\\b").Count);
        Assert.Single(Regex.Matches(page, "<FlightListLegend\\b"));
        Assert.DoesNotContain("bi-exclamation-triangle-fill", page);
        Assert.DoesNotContain("bi-lock-fill", page);
    }

    // The header's first column and every row's must match, or the two slots drift out of line with the headings.
    [Fact]
    public void The_list_header_and_rows_share_one_grid()
    {
        var page = Source("FlightManagement.razor");

        var grids = Regex.Matches(page, "grid-template-columns:\\s*([^;\"]+)").Select(m => m.Groups[1].Value.Trim()).Distinct().ToArray();
        Assert.Single(grids);
        Assert.Equal("@FlightRowIcons.ListGrid", grids[0]);
    }

    [Fact]
    public void There_is_no_hidden_filter()
    {
        foreach (var file in new[] { "FlightManagement.razor", "FlightRowIcons.razor", "FlightListLegend.razor" })
            Assert.DoesNotMatch(new Regex("Hidden somewhere|hidden filter|HiddenOnly|filterHidden", RegexOptions.IgnoreCase), Source(file));
    }

    // ---- Helpers ----

    private static Task<InteractiveRenderer> Icons(FEDepartureFlight flight, Action? onPadlock = null) =>
        Icons(FlightListCue.For(flight), flight.FLT, onPadlock);

    private static Task<InteractiveRenderer> Icons(FEArrivalFlight flight, Action? onPadlock = null) =>
        Icons(FlightListCue.For(flight), flight.FLT, onPadlock);

    private static Task<InteractiveRenderer> Icons(FlightListCue cue, string? flight, Action? onPadlock) =>
        InteractiveRenderer.RenderAsync<FlightRowIcons>(new Dictionary<string, object?>
        {
            [nameof(FlightRowIcons.Cue)] = cue,
            [nameof(FlightRowIcons.Flight)] = flight,
            [nameof(FlightRowIcons.OnPadlockClick)] = EventCallback.Factory.Create(new object(), onPadlock ?? (() => { })),
        });

    private static string Text(string html) =>
        Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), "\\s+", " ").Trim();

    // The inner markup of the one element whose class list holds the class.
    private static string Single(string html, string cssClass) =>
        Assert.Single(Regex.Matches(html, $"<(\\w+) class=\"(?:[^\"]* )?{cssClass}(?: [^\"]*)?\"[^>]*>(.*?)</\\1>")).Groups[2].Value;

    private static string[] PanelRows(string html) =>
        Regex.Matches(html, "<li[^>]*>(.*?)</li>").Select(m => Text(m.Groups[1].Value)).ToArray();

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DXSuite.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "DXSuite.Aviation", "Pages", "FlightDataEditor", file));
    }

    private static FEDepartureFlight Departure(OverrideDepartureFlight? edit = null, bool preserved = false,
        string? rop = On, string? rob = On, string? ros = On, string? rot = On) => new()
    {
        RecordId = 17,
        FLT = "AC472",
        ROP = rop,
        ROB = rob,
        ROS = ros,
        ROT = rot,
        overrideFlightData = edit,
        preserveOverride = preserved,
    };

    private static FEArrivalFlight Arrival(OverrideArrivalFlight? edit = null, bool preserved = false,
        string? rop = On, string? rob = On, string? ros = On, string? rot = On) => new()
    {
        RecordId = 18,
        FLT = "AC473",
        ROP = rop,
        ROB = rob,
        ROS = ros,
        ROT = rot,
        overrideFlightData = edit,
        preserveOverride = preserved,
    };
}
