using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services;
using DXSuite.Sentinel.Services.Pulse;

namespace DXSuite.Sentinel.Tests;

/// <summary>
/// Pins how an issue type's ErrorLevel and Pulse monitor item resolve: a monitor point's own override,
/// then its severity default's override, then the shipped value, with level and item resolving
/// independently. Also pins the shipped catalogue and the open issue code formats.
/// </summary>
public class PulseIssueLevelResolutionTests
{
    [Theory]
    [InlineData("web-content-status", "warning", 2)]
    [InlineData("web-content-status", "critical", 3)]
    [InlineData("console-errors", "warning", 2)]
    [InlineData("cpu-percent", "critical", 3)]
    [InlineData("disk-used-percent", "warning", 2)]
    public void Catalogue_monitor_points_report_at_their_shipped_level_on_item_zero(string point, string severity, byte expected)
    {
        var r = PulseIssueLevelService.Resolve(PulseIssueKey.MonitorPoint(point, severity), PulseIssueOverrides.None);

        Assert.Equal(new PulseIssueResolution(expected, 0, LevelOverridden: false, ItemOverridden: false), r);
    }

    [Fact]
    public void Url_missing_ships_at_three_and_source_offline_at_two()
    {
        Assert.Equal((byte)3, PulseIssueLevelService.Resolve(PulseIssueKey.DirectorUrlMissing, PulseIssueOverrides.None).ErrorLevel);
        Assert.Equal((byte)2, PulseIssueLevelService.Resolve(PulseIssueKey.SourceOffline, PulseIssueOverrides.None).ErrorLevel);
    }

    [Fact]
    public void The_catalogue_has_no_connector_probe_issue_type()
    {
        Assert.DoesNotContain(PulseIssueCatalogue.Entries, e => e.Key.DirectorAlertKind is not null && e.Key.DirectorAlertKind != PulseIssueKey.UrlMissing);
        Assert.Single(PulseIssueCatalogue.Entries, e => e.Key.Kind == PulseIssueKind.DirectorAlert);
    }

    [Fact]
    public void An_unknown_monitor_point_falls_to_its_severity_default()
    {
        Assert.Equal((byte)2, Resolve(PulseIssueKey.MonitorPoint("fan-rpm", "warning")).ErrorLevel);
        Assert.Equal((byte)3, Resolve(PulseIssueKey.MonitorPoint("fan-rpm", "critical")).ErrorLevel);
        Assert.Equal((byte)4, Resolve(PulseIssueKey.MonitorPoint("fan-rpm", "critical"),
            Row(PulseIssueKind.SeverityDefault, severity: "critical", level: 4)).ErrorLevel);
    }

    [Fact]
    public void A_severity_default_override_applies_to_catalogue_points_without_their_own_row()
    {
        var r = Resolve(PulseIssueKey.MonitorPoint("cpu-percent", "warning"), Row(PulseIssueKind.SeverityDefault, severity: "warning", level: 1));

        Assert.Equal((byte)1, r.ErrorLevel);
        Assert.True(r.LevelOverridden);
    }

    [Fact]
    public void A_monitor_point_row_beats_its_severity_default_row()
    {
        var r = Resolve(PulseIssueKey.MonitorPoint("cpu-percent", "warning"),
            Row(PulseIssueKind.SeverityDefault, severity: "warning", level: 1),
            Row(PulseIssueKind.MonitorPoint, "cpu-percent", "warning", level: 4));

        Assert.Equal((byte)4, r.ErrorLevel);
    }

    [Fact]
    public void A_do_not_report_row_is_not_reported()
    {
        var r = Resolve(PulseIssueKey.SourceOffline, Row(PulseIssueKind.SourceOffline, level: null, doNotReport: true));

        Assert.False(r.IsReported);
        Assert.Null(r.ErrorLevel);
        Assert.True(r.LevelOverridden);
    }

    [Fact]
    public void A_row_that_only_moves_the_item_keeps_following_the_severity_default_level()
    {
        var itemOnly = Row(PulseIssueKind.MonitorPoint, "disk-used-percent", "critical", level: null, item: 80);
        var key = PulseIssueKey.MonitorPoint("disk-used-percent", "critical");

        Assert.Equal(new PulseIssueResolution(3, 80, LevelOverridden: false, ItemOverridden: true), Resolve(key, itemOnly));

        var criticalsRaised = Row(PulseIssueKind.SeverityDefault, severity: "critical", level: 4);
        Assert.Equal(new PulseIssueResolution(4, 80, LevelOverridden: true, ItemOverridden: true), Resolve(key, itemOnly, criticalsRaised));
    }

    [Fact]
    public void A_do_not_report_severity_default_silences_points_without_their_own_level()
    {
        var silenced = Row(PulseIssueKind.SeverityDefault, severity: "warning", level: null, doNotReport: true);

        Assert.False(Resolve(PulseIssueKey.MonitorPoint("cpu-percent", "warning"), silenced).IsReported);
        Assert.False(Resolve(PulseIssueKey.MonitorPoint("cpu-percent", "warning"), silenced,
            Row(PulseIssueKind.MonitorPoint, "cpu-percent", "warning", level: null, item: 5)).IsReported);
        Assert.Equal((byte)2, Resolve(PulseIssueKey.MonitorPoint("cpu-percent", "warning"), silenced,
            Row(PulseIssueKind.MonitorPoint, "cpu-percent", "warning", level: 2)).ErrorLevel);
    }

    [Fact]
    public void A_monitor_point_do_not_report_beats_a_severity_default_level()
    {
        var r = Resolve(PulseIssueKey.MonitorPoint("console-errors", "warning"),
            Row(PulseIssueKind.MonitorPoint, "console-errors", "warning", level: null, doNotReport: true),
            Row(PulseIssueKind.SeverityDefault, severity: "warning", level: 4));

        Assert.False(r.IsReported);
    }

    [Fact]
    public void Level_and_item_resolve_independently_along_the_chain()
    {
        var r = Resolve(PulseIssueKey.MonitorPoint("disk-used-percent", "critical"),
            Row(PulseIssueKind.MonitorPoint, "disk-used-percent", "critical", level: 4, item: null),
            Row(PulseIssueKind.SeverityDefault, severity: "critical", level: 1, item: 12));

        Assert.Equal(new PulseIssueResolution(4, 12, LevelOverridden: true, ItemOverridden: true), r);
    }

    [Fact]
    public void The_item_chain_ends_at_zero()
    {
        var r = Resolve(PulseIssueKey.MonitorPoint("disk-used-percent", "critical"),
            Row(PulseIssueKind.MonitorPoint, "disk-used-percent", "critical", level: 4));

        Assert.Equal(0, r.MonitorItemId);
        Assert.False(r.ItemOverridden);
    }

    [Fact]
    public void A_severity_default_row_does_not_reach_source_offline_or_url_missing()
    {
        var sevDefault = Row(PulseIssueKind.SeverityDefault, severity: "warning", level: 1, item: 9);

        Assert.Equal(new PulseIssueResolution(2, 0, false, false), Resolve(PulseIssueKey.SourceOffline, sevDefault));
        Assert.Equal(new PulseIssueResolution(3, 0, false, false), Resolve(PulseIssueKey.DirectorUrlMissing, sevDefault));
    }

    [Fact]
    public void Url_missing_resolves_from_its_director_alert_row()
    {
        var row = new SentinelPulseIssueLevel { Kind = PulseIssueKind.DirectorAlert, DirectorAlertKind = PulseIssueKey.UrlMissing, ErrorLevel = 4, MonitorItemId = 3 };

        Assert.Equal(new PulseIssueResolution(4, 3, true, true), Resolve(PulseIssueKey.DirectorUrlMissing, row));
    }

    [Fact]
    public void An_unrecognised_severity_is_a_warning()
    {
        var issue = OpenIssue.FromAlert(new ActiveAlert { MonitorPointId = "input-status", Severity = "Minor", Message = "Backgrounded" });

        Assert.Equal("input-status/warning", issue.Code);
        Assert.Equal("Backgrounded (warning)", issue.Summary);
        Assert.Equal((byte)2, Resolve(issue.Key).ErrorLevel);
    }

    [Fact]
    public void Qualified_codes_share_one_lookup_row_per_kind()
    {
        var url = OpenIssue.UrlMissing("kiosk-3", "https://x", null);
        var source = OpenIssue.SourceOffline("cloud-connector", "Source offline: connector 5", null);

        Assert.Equal("UrlMissing:kiosk-3", url.Code);
        Assert.Equal("SourceOffline:cloud-connector", source.Code);
        Assert.Equal(PulseIssueKey.DirectorUrlMissing, url.Key);
        Assert.Equal(PulseIssueKey.SourceOffline, source.Key);

        var row = Row(PulseIssueKind.SourceOffline, level: 1);
        Assert.Equal((byte)1, Resolve(OpenIssue.SourceOffline("gateway", "g", null).Key, row).ErrorLevel);
        Assert.Equal((byte)1, Resolve(source.Key, row).ErrorLevel);
    }

    [Theory]
    [InlineData("UrlMissing:kiosk-3", "UrlMissing")]
    [InlineData("SourceOffline:cloud-connector", "SourceOffline")]
    [InlineData("cpu-percent/critical", "cpu-percent/critical")]
    public void Strip_qualifier_removes_everything_after_the_first_colon(string code, string expected)
    {
        Assert.Equal(expected, OpenIssue.StripQualifier(code));
    }

    [Fact]
    public void Override_rows_are_matched_whatever_the_stored_severity_case()
    {
        var r = Resolve(PulseIssueKey.MonitorPoint("cpu-percent", "critical"), Row(PulseIssueKind.MonitorPoint, "cpu-percent", "Critical", level: 1));

        Assert.Equal((byte)1, r.ErrorLevel);
    }

    private static PulseIssueResolution Resolve(PulseIssueKey key, params SentinelPulseIssueLevel[] rows) =>
        PulseIssueLevelService.Resolve(key, new PulseIssueOverrides(rows));

    private static SentinelPulseIssueLevel Row(string kind, string? monitorPointId = null, string? severity = null, byte? level = 2, int? item = null, bool doNotReport = false) => new()
    {
        Kind = kind,
        MonitorPointId = monitorPointId,
        Severity = severity,
        ErrorLevel = level,
        DoNotReport = doNotReport,
        MonitorItemId = item,
    };
}
