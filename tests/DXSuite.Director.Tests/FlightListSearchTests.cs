using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-10 (B-21): search by flight number spans the whole window, before paging, so a flight on a later page is
/// found from any page.
/// </summary>
public class FlightListSearchTests
{
    private const int PageSize = 20;

    // 60 arrivals AC100..AC159 in list order: AC145 is on page 3.
    private static List<FEArrivalFlight?> Arrivals() =>
        Enumerable.Range(100, 60).Select(n => (FEArrivalFlight?)Arrival("AC", n)).ToList();

    [Fact]
    public void Search_finds_a_flight_on_page_3()
    {
        var all = Arrivals();
        Assert.DoesNotContain(FlightListSearch.Page(FlightListSearch.Filter(all, ""), 1, PageSize), f => f.FLT == "AC145");

        var found = FlightListSearch.Filter(all, "AC145");

        var shown = FlightListSearch.Page(found, 1, PageSize);
        Assert.Equal("AC145", Assert.Single(shown).FLT);
    }

    // A search typed while a later page is open shows its matches rather than an empty page.
    [Fact]
    public void A_search_typed_on_page_3_shows_its_matches()
    {
        var found = FlightListSearch.Filter(Arrivals(), "AC1");

        var shown = FlightListSearch.Page(found, 3, PageSize);

        Assert.Equal(["AC140", "AC141", "AC142", "AC143", "AC144", "AC145", "AC146", "AC147", "AC148", "AC149",
            "AC150", "AC151", "AC152", "AC153", "AC154", "AC155", "AC156", "AC157", "AC158", "AC159"], shown.Select(f => f.FLT));
        Assert.Equal(["AC145"], FlightListSearch.Page(FlightListSearch.Filter(Arrivals(), "AC145"), 3, PageSize).Select(f => f.FLT));
    }

    [Theory]
    [InlineData("ac145")]
    [InlineData("AC 145")]
    [InlineData(" 145 ")]
    public void The_flight_number_matches_whatever_its_case_and_spacing(string typed)
    {
        Assert.Equal("AC145", Assert.Single(FlightListSearch.Filter(Arrivals(), typed)).FLT);
    }

    [Fact]
    public void A_code_share_number_finds_its_flight()
    {
        var shared = Arrival("AC", 145);
        shared.CodeShares.Add(new FECodeShare { FLC = "UA", FLN = "8012" });
        var departures = new List<FEDepartureFlight?> { Departure("WS", 700), new FEDepartureFlight { FLT = "AC145", FLC = "AC", FLN = "145", CodeShares = { new FECodeShare { FLC = "LH", FLN = "6801" } } } };

        Assert.Equal("AC145", Assert.Single(FlightListSearch.Filter([shared, Arrival("WS", 1)], "UA8012")).FLT);
        Assert.Equal("AC145", Assert.Single(FlightListSearch.Filter(departures, "lh 6801")).FLT);
    }

    // The search is by flight number (FSD 1.1 UC 5): a gate, status or city that contains the text is no match.
    [Fact]
    public void Only_the_flight_number_is_searched()
    {
        var flight = Departure("AC", 145);
        flight.GAT = "D27";
        flight.RCP = "DELAYED";
        flight.CTY = "Dallas";

        Assert.Empty(FlightListSearch.Filter([flight], "D27"));
        Assert.Empty(FlightListSearch.Filter([flight], "DELAYED"));
        Assert.Empty(FlightListSearch.Filter([flight], "Dallas"));
    }

    [Fact]
    public void An_empty_search_keeps_every_flight_in_order_and_drops_empty_entries()
    {
        var all = Arrivals();
        all.Insert(10, null);

        var kept = FlightListSearch.Filter(all, "  ");

        Assert.Equal(Enumerable.Range(100, 60).Select(n => $"AC{n}"), kept.Select(f => f.FLT));
    }

    private static FEArrivalFlight Arrival(string carrier, int number) =>
        new() { FLT = $"{carrier}{number}", FLC = carrier, FLN = number.ToString() };

    private static FEDepartureFlight Departure(string carrier, int number) =>
        new() { FLT = $"{carrier}{number}", FLC = carrier, FLN = number.ToString() };
}
