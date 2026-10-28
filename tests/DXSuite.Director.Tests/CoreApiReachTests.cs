using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;

namespace DXSuite.Director.Tests;

// Section numbers and clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// FDE-11 (B-25): when the Flight Data Editor counts CoreAPI as unreachable, from its own calls and from the CoreAPI
/// watcher's stored state, and when it recovers.
/// </summary>
public class CoreApiReachTests
{
    private static readonly DateTime T0 = new(2026, 10, 19, 14, 0, 0, DateTimeKind.Utc);

    private static CoreApiResult<string> Result(CoreApiOutcome outcome) => new(outcome);

    [Fact]
    public void A_call_that_found_no_server_makes_CoreAPI_unreachable()
    {
        var reach = new CoreApiReach().After(Result(CoreApiOutcome.Unreachable));

        Assert.True(reach.Unreachable);
    }

    [Theory]
    [InlineData(CoreApiOutcome.Succeeded)]
    [InlineData(CoreApiOutcome.Failed)]
    public void Any_answer_from_a_server_recovers(CoreApiOutcome answered)
    {
        var reach = new CoreApiReach().After(Result(CoreApiOutcome.Unreachable)).After(Result(answered));

        Assert.False(reach.Unreachable);
    }

    [Fact]
    public void A_call_that_was_never_sent_says_nothing_about_the_servers()
    {
        var unreachable = new CoreApiReach().After(Result(CoreApiOutcome.Unreachable));

        Assert.True(unreachable.After(Result(CoreApiOutcome.NotConfigured)).Unreachable);
        Assert.False(new CoreApiReach().After(Result(CoreApiOutcome.NotConfigured)).Unreachable);
    }

    [Fact]
    public void The_watcher_finding_no_server_makes_CoreAPI_unreachable_and_its_next_answer_recovers()
    {
        var reach = new CoreApiReach().After(new CoreApiFeedState("OK", Reachable: false, null, T0));
        Assert.True(reach.Unreachable);

        reach = reach.After(new CoreApiFeedState("OK", Reachable: true, null, T0.AddSeconds(30)));
        Assert.False(reach.Unreachable);
    }

    [Fact]
    public void Re_reading_the_same_watcher_state_does_not_undo_a_later_call()
    {
        var watcherDown = new CoreApiFeedState("OK", Reachable: false, null, T0);
        var reach = new CoreApiReach().After(watcherDown).After(Result(CoreApiOutcome.Succeeded));

        Assert.False(reach.After(watcherDown).Unreachable);

        var watcherUp = new CoreApiFeedState("OK", Reachable: true, null, T0.AddSeconds(30));
        reach = reach.After(watcherUp).After(Result(CoreApiOutcome.Unreachable));
        Assert.True(reach.After(watcherUp).Unreachable);
    }

    [Fact]
    public void No_watcher_state_yet_changes_nothing()
    {
        var unreachable = new CoreApiReach().After(Result(CoreApiOutcome.Unreachable));

        Assert.True(unreachable.After(null).Unreachable);
        Assert.False(new CoreApiReach().After(null).Unreachable);
    }
}
