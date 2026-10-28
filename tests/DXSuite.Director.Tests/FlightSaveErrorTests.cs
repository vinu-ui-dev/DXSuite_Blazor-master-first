using System.Net;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;

namespace DXSuite.Director.Tests;

/// <summary>
/// FDE-06 (nothing fails silently): the editor words every failed CoreAPI call one way, with the same reason the
/// call's User event records.
/// </summary>
public class FlightSaveErrorTests
{
    public static TheoryData<CoreApiResult<int>, string, string> Cases => new()
    {
        { new(CoreApiOutcome.Failed, StatusCode: HttpStatusCode.Conflict, Error: "Preserved edits cannot become Outage edits"),
            "Could not save the flight: CoreAPI answered 409 Preserved edits cannot become Outage edits.", "409 Preserved edits cannot become Outage edits" },
        { new(CoreApiOutcome.Failed), "Could not save the flight: CoreAPI answered.", "" },
        { new(CoreApiOutcome.Unreachable), "Could not save the flight: no CoreAPI server answered.", "no CoreAPI server answered" },
        { new(CoreApiOutcome.NotConfigured), "Could not save the flight: the CoreAPI connection is not set up.", "the CoreAPI connection is not set up" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_failed_call_reads_the_same_on_the_page_and_in_the_event_log(CoreApiResult<int> result, string shown, string logged)
    {
        Assert.Equal(shown, FlightSaveError.Describe(result));
        Assert.Equal(logged, CoreApiEventOutcome.Error(result));
    }

    [Fact]
    public void The_page_words_its_loads_and_the_switch_with_the_same_helper()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DXSuite.Aviation", "Pages", "FlightDataEditor", "FlightManagement.razor"));

        Assert.DoesNotContain("CoreAPI answered", source);
        Assert.Contains("FlightSaveError.Describe(result, $\"Could not load {what}\")", source);
        Assert.Contains("FlightSaveError.Describe(result, $\"Could not switch flight Outage mode", source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DXSuite.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
