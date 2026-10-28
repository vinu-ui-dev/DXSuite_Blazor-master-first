using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Pulse;

namespace DXSuite.Sentinel.Tests;

/// <summary>
/// Pins how the Pulse Reporting tab's Level and Monitor item selects map onto <c>SentinelPulseIssueLevel</c>
/// columns, and which chosen items a save refuses.
/// </summary>
public class PulseIssueLevelChoiceTests
{
    private static readonly PulseIssueKey CpuWarning = PulseIssueKey.MonitorPoint("cpu-percent", PulseIssueKey.Warning);

    [Theory]
    [InlineData("", "", null, false, null)]
    [InlineData("3", "", (byte)3, false, null)]
    [InlineData("dnr", "", null, true, null)]
    [InlineData("", "12", null, false, 12)]
    [InlineData("4", "0", (byte)4, false, 0)]
    public void Select_values_map_to_columns(string level, string item, byte? errorLevel, bool doNotReport, int? monitorItemId)
    {
        var choice = new PulseIssueLevelChoice(level, item);

        Assert.Equal(errorLevel, choice.ErrorLevel);
        Assert.Equal(doNotReport, choice.DoNotReport);
        Assert.Equal(monitorItemId, choice.MonitorItemId);
    }

    [Fact]
    public void Both_selects_on_default_keep_no_row()
    {
        Assert.True(PulseIssueLevelChoice.Default.IsDefault);
        Assert.Null(PulseIssueLevelChoice.Default.ToRow(CpuWarning));
    }

    [Fact]
    public void An_item_of_zero_is_an_override_not_a_default()
    {
        var row = new PulseIssueLevelChoice("", "0").ToRow(CpuWarning);

        Assert.NotNull(row);
        Assert.Equal(0, row.MonitorItemId);
        Assert.Null(row.ErrorLevel);
    }

    [Fact]
    public void A_row_round_trips_through_the_selects()
    {
        var row = new SentinelPulseIssueLevel
        {
            Kind = PulseIssueKind.MonitorPoint, MonitorPointId = "cpu-percent", Severity = "warning",
            DoNotReport = true, MonitorItemId = 7,
        };

        var choice = PulseIssueLevelChoice.FromRow(row);

        Assert.Equal(new PulseIssueLevelChoice("dnr", "7"), choice);
        var back = choice.ToRow(CpuWarning)!;
        Assert.True(back.DoNotReport);
        Assert.Null(back.ErrorLevel);
        Assert.Equal(7, back.MonitorItemId);
        Assert.Equal(PulseIssueLevelService.KeyOf(row), PulseIssueLevelService.KeyOf(back));
    }

    [Fact]
    public void Draft_rows_resolve_like_saved_rows()
    {
        var severityDefault = PulseIssueKey.SeverityDefault(PulseIssueKey.Warning);
        var overrides = new PulseIssueOverrides([new PulseIssueLevelChoice("4", "").ToRow(severityDefault)!]);

        Assert.Equal((byte)4, PulseIssueLevelService.Resolve(CpuWarning, overrides).ErrorLevel);
    }

    [Fact]
    public void Save_refuses_rows_naming_an_item_pulse_does_not_have()
    {
        var urlMissing = PulseIssueKey.DirectorUrlMissing;
        var choices = new Dictionary<PulseIssueKey, PulseIssueLevelChoice>
        {
            [CpuWarning] = new("3", "99"),
            [urlMissing] = new("", "12"),
            [PulseIssueKey.SourceOffline] = new("2", ""),
        };

        var missing = PulseMonitorItems.MissingItems(choices, new HashSet<int> { 0, 12 });

        Assert.Equal([CpuWarning], missing);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(10, true)]
    [InlineData(13, true)]
    [InlineData(25, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(12, false)]
    [InlineData(26, false)]
    public void Agent_items_are_one_to_four_ten_and_thirteen_to_twentyfive(int item, bool isAgent)
    {
        Assert.Equal(isAgent, PulseMonitorItems.AgentItemIds.Contains(item));
    }
}
