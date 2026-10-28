using System.Text.Json;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services;
using DXSuite.Sentinel.Services.Pulse;

namespace DXSuite.Sentinel.Tests;

/// <summary>
/// Pins what the Pulse publisher writes for one linked device in one cycle: heartbeat eligibility and
/// value, which open issues exist, and when a reading is written. Each fact comes from the test strategy
/// in docs/spec/sentinel-pulse-reporting.md section 10.
/// </summary>
public class PulsePublishPlannerTests
{
    private const int DeviceId = 42;
    private const string PulseDevice = "LOBBY-01";
    private const string Customer = "RAD";
    private static readonly DateTime Now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    // Heartbeat

    [Fact]
    public void Dead_device_writes_no_heartbeat_and_no_source_offline_although_its_stored_state_says_healthy()
    {
        var lastSeen = Now.AddMinutes(-20);
        var sources = new[]
        {
            Source("heartbeat", "D1", OverallHealthStatus.Healthy, lastSeen),
            Source("gateway", "3", OverallHealthStatus.Healthy, lastSeen),
        };

        // LastStateJson is recomputed only when a source reports, so it still says what it said then.
        Assert.Equal(OverallHealthStatus.Healthy, DeviceHealthRollup.BuildState(sources, lastSeen).OverallHealth);

        var plan = Plan(sources);

        Assert.False(plan.HeartbeatEligible);
        Assert.Null(plan.HeartbeatUtc);
        Assert.Equal(PulsePublishPlanner.SuppressedOffline, plan.SuppressionReason);
        Assert.DoesNotContain(plan.Issues, i => i.Issue.Key == PulseIssueKey.SourceOffline);
        Assert.Empty(plan.Readings);
    }

    [Fact]
    public void Fresh_evidence_older_than_five_minutes_suppresses_the_heartbeat_as_stale_evidence()
    {
        var plan = Plan([Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddMinutes(-6))]);

        Assert.Null(plan.HeartbeatUtc);
        Assert.Equal(PulsePublishPlanner.SuppressedStaleEvidence, plan.SuppressionReason);
    }

    [Fact]
    public void Heartbeat_carries_the_newest_fresh_evidence_not_the_cycle_stamp()
    {
        var newest = Now.AddSeconds(-95);
        var plan = Plan(
        [
            Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddMinutes(-4)),
            Source("gateway", "3", OverallHealthStatus.Warning, newest),
        ]);

        Assert.True(plan.HeartbeatEligible);
        Assert.Equal(newest, plan.HeartbeatUtc);
        Assert.Null(plan.SuppressionReason);
    }

    [Fact]
    public void Heartbeat_is_never_later_than_the_cycle_stamp()
    {
        var plan = Plan([Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddSeconds(30))]);

        Assert.Equal(Now, plan.HeartbeatUtc);
    }

    [Fact]
    public void Unknown_health_suppresses_the_heartbeat()
    {
        Assert.Equal(PulsePublishPlanner.SuppressedUnknown,
            Plan([Source("heartbeat", "D1", OverallHealthStatus.Unknown, Now.AddSeconds(-10))]).SuppressionReason);
        Assert.Equal(PulsePublishPlanner.SuppressedUnknown, Plan([]).SuppressionReason);
    }

    [Fact]
    public void A_heartbeat_already_sent_is_not_sent_again_but_the_device_stays_eligible()
    {
        var seen = Now.AddSeconds(-40);
        var plan = Plan([Source("heartbeat", "D1", OverallHealthStatus.Healthy, seen)],
            device: DeviceStateRow(lastHeartbeat: seen));

        Assert.True(plan.HeartbeatEligible);
        Assert.Equal(seen, plan.EligibleHeartbeatUtc);
        Assert.Null(plan.HeartbeatUtc);
    }

    // Worst-of per monitor item

    [Fact]
    public void Two_issues_on_one_item_publish_once_at_the_worse_level()
    {
        var plan = Plan([AgentHost(Alert("cpu-percent", "warning", "CPU is 88%"), Alert("disk-used-percent", "critical", "Disk used is 96%"))]);

        var reading = Assert.Single(plan.Readings);
        Assert.Equal(0, reading.MonitorItemId);
        Assert.Equal(3, reading.ErrorLevel);
        Assert.Equal(2, reading.OpenIssueCount);
        Assert.Equal("cpu-percent/warning;disk-used-percent/critical", reading.OpenIssueCodes);
        Assert.Equal("Disk used is 96% (critical); CPU is 88% (warning)", reading.Summary);
    }

    [Fact]
    public void Clearing_one_of_two_issues_writes_the_survivor_not_a_clear()
    {
        var plan = Plan([AgentHost(Alert("cpu-percent", "warning", "CPU is 88%"))],
            items: [ItemState(0, 3, "cpu-percent/warning;disk-used-percent/critical")]);

        var reading = Assert.Single(plan.Readings);
        Assert.Equal(2, reading.ErrorLevel);
        Assert.Equal("cpu-percent/warning", reading.OpenIssueCodes);
        Assert.Equal(PulseReadingReason.Changed, reading.Reason);
    }

    [Fact]
    public void An_item_whose_issues_all_closed_is_written_once_as_a_clear()
    {
        var healthy = new[] { Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddSeconds(-20)) };

        var reading = Assert.Single(Plan(healthy, items: [ItemState(0, 3, "disk-used-percent/critical")]).Readings);
        Assert.True(reading.IsClear);
        Assert.Equal(PulsePublishPlanner.ClearSummary, reading.Summary);
        Assert.Equal(0, reading.OpenIssueCount);
        Assert.Null(reading.OldestTriggeredUtc);
        Assert.Equal("", reading.OpenIssueCodes);

        Assert.Empty(Plan(healthy, items: [ItemState(0, 0, "")]).Readings);
    }

    // Qualified codes

    [Fact]
    public void A_second_missing_url_on_the_same_device_changes_the_code_set_and_writes()
    {
        var urls = new[] { Url("kiosk-1", missing: true), Url("kiosk-2", missing: true) };

        var reading = Assert.Single(Plan([Healthy()], urls: urls, items: [ItemState(0, 3, "UrlMissing:kiosk-1")]).Readings);
        Assert.Equal(3, reading.ErrorLevel);
        Assert.Equal("UrlMissing:kiosk-1;UrlMissing:kiosk-2", reading.OpenIssueCodes);
    }

    [Fact]
    public void A_second_source_going_offline_changes_the_code_set_and_writes()
    {
        var sources = new[]
        {
            Source("gateway", "3", OverallHealthStatus.Healthy, Now.AddSeconds(-30)),
            Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddMinutes(-15)),
            Source("agent-host", "host-7", OverallHealthStatus.Healthy, Now.AddMinutes(-12)),
        };

        var reading = Assert.Single(Plan(sources, items: [ItemState(0, 2, "SourceOffline:heartbeat")]).Readings);
        Assert.Equal("SourceOffline:agent-host;SourceOffline:heartbeat", reading.OpenIssueCodes);
        Assert.Equal("Source offline: agent (host-7); Source offline: Engage heartbeat (D1)", reading.Summary);
    }

    [Fact]
    public void Source_offline_names_the_connector_and_the_gateway()
    {
        var names = new PulseSourceNames(
            new Dictionary<int, PulseConnectorName> { [5] = new("Pixera", "SLC Ticket Wall") },
            new Dictionary<int, string> { [3] = "Terminal 1" });
        var sources = new[]
        {
            Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddSeconds(-30)),
            Source("cloud-connector", "5:SYS-9", OverallHealthStatus.Healthy, Now.AddMinutes(-11)),
            Source("gateway", "3", OverallHealthStatus.Healthy, Now.AddMinutes(-11)),
        };

        var reading = Assert.Single(Plan(sources, names: names).Readings);
        Assert.Equal("Source offline: Pixera 'SLC Ticket Wall'; Source offline: gateway 'Terminal 1'", reading.Summary);
        Assert.Equal(Now.AddMinutes(-1), reading.OldestTriggeredUtc);
    }

    [Fact]
    public void A_stale_gateway_angle_on_the_direct_probe_is_named_as_such()
    {
        var reading = Assert.Single(Plan(
        [
            Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddSeconds(-30)),
            Source("gateway", "direct", OverallHealthStatus.Healthy, Now.AddMinutes(-11)),
        ]).Readings);

        Assert.Equal("Source offline: direct probe", reading.Summary);
    }

    [Fact]
    public void A_url_that_is_not_monitored_raises_nothing()
    {
        Assert.Empty(Plan([Healthy()], urls: [Url("kiosk-1", missing: true, monitored: false), Url("kiosk-2", missing: false)]).Readings);
    }

    // What counts as a change

    [Fact]
    public void A_summary_only_change_writes_nothing()
    {
        var plan = Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 97%"))],
            items: [ItemState(0, 3, "disk-used-percent/critical", summary: "Disk used is 96% (critical)")]);

        Assert.Empty(plan.Readings);
    }

    [Fact]
    public void A_level_override_change_rewrites_the_item_at_the_new_level()
    {
        var overrides = Overrides(Row(PulseIssueKind.MonitorPoint, "disk-used-percent", "critical", level: 4));

        var reading = Assert.Single(Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%"))],
            overrides: overrides, items: [ItemState(0, 3, "disk-used-percent/critical")]).Readings);

        Assert.Equal(4, reading.ErrorLevel);
        Assert.Equal(PulseReadingReason.Changed, reading.Reason);
    }

    [Fact]
    public void Do_not_report_on_the_last_open_issue_clears_the_item()
    {
        var overrides = Overrides(Row(PulseIssueKind.MonitorPoint, "disk-used-percent", "critical", level: null, doNotReport: true));

        var plan = Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%"))],
            overrides: overrides, items: [ItemState(0, 3, "disk-used-percent/critical")]);

        Assert.Empty(plan.Issues);
        Assert.True(Assert.Single(plan.Readings).IsClear);
    }

    [Fact]
    public void A_monitor_item_move_clears_the_old_item_and_opens_the_new_one()
    {
        var overrides = Overrides(Row(PulseIssueKind.MonitorPoint, "disk-used-percent", "critical", level: 3, item: 7));

        var plan = Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%"))],
            overrides: overrides, items: [ItemState(0, 3, "disk-used-percent/critical")]);

        Assert.Collection(plan.Readings,
            old => { Assert.Equal(0, old.MonitorItemId); Assert.True(old.IsClear); },
            moved => { Assert.Equal(7, moved.MonitorItemId); Assert.Equal(3, moved.ErrorLevel); });
    }

    // First run and relink

    [Fact]
    public void First_run_writes_the_heartbeat_and_only_items_with_open_issues()
    {
        Assert.Empty(Plan([Healthy()]).Readings);

        var overrides = Overrides(Row(PulseIssueKind.SourceOffline, level: 2, item: 5));
        var plan = Plan([AgentHost(Alert("cpu-percent", "warning", "CPU is 88%"))], overrides: overrides);

        Assert.NotNull(plan.HeartbeatUtc);
        var reading = Assert.Single(plan.Readings);
        Assert.Equal(0, reading.MonitorItemId);
    }

    [Fact]
    public void Relink_discards_item_state_and_writes_nothing_for_the_old_device()
    {
        var seen = Now.AddSeconds(-20);
        var plan = Plan([Source("heartbeat", "D1", OverallHealthStatus.Healthy, seen)],
            device: DeviceStateRow(pulseDeviceId: "OLD-01", lastHeartbeat: seen, linkBroken: Now.AddHours(-1)),
            items: [ItemState(0, 3, "disk-used-percent/critical")]);

        Assert.True(plan.Relinked);
        Assert.False(plan.LinkBroken);
        Assert.Empty(plan.Readings);
        Assert.Equal(seen, plan.HeartbeatUtc);
        Assert.Equal(PulseDevice, plan.DeviceState!.PulseDeviceId);
        Assert.Null(plan.DeviceState.LinkBrokenUtc);
        Assert.Null(plan.DeviceState.LastHeartbeatSentUtc);
    }

    [Fact]
    public void A_customer_change_is_a_relink()
    {
        var plan = Plan([Healthy()], device: DeviceStateRow(customer: "SNT"), items: [ItemState(0, 3, "x/critical")]);

        Assert.True(plan.Relinked);
        Assert.Equal(Customer, plan.DeviceState!.PulseCustomerId);
    }

    // Retention

    [Fact]
    public void An_open_item_older_than_the_reassert_interval_is_reasserted()
    {
        var plan = Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%"))],
            items: [ItemState(0, 3, "disk-used-percent/critical", published: Now.AddHours(-25))]);

        Assert.Equal(PulseReadingReason.Reassert, Assert.Single(plan.Readings).Reason);
    }

    [Fact]
    public void An_open_item_inside_the_reassert_interval_is_not_rewritten()
    {
        Assert.Empty(Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%"))],
            items: [ItemState(0, 3, "disk-used-percent/critical", published: Now.AddHours(-23))]).Readings);
    }

    [Fact]
    public void A_cleared_item_is_never_reasserted()
    {
        Assert.Empty(Plan([Healthy()], items: [ItemState(0, 0, "", published: Now.AddDays(-30))]).Readings);
    }

    // Link broken

    [Fact]
    public void A_device_marked_link_broken_produces_no_writes()
    {
        var plan = Plan([AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%"))],
            device: DeviceStateRow(linkBroken: Now.AddMinutes(-5)),
            items: [ItemState(0, 0, "")]);

        Assert.True(plan.LinkBroken);
        Assert.False(plan.HasWrites);
    }

    // Rendering and bookkeeping

    [Fact]
    public void The_summary_is_capped_at_500_characters_with_an_ellipsis()
    {
        var urls = Enumerable.Range(1, 40).Select(i => Url($"kiosk-{i:00}", missing: true)).ToArray();

        var reading = Assert.Single(Plan([Healthy()], urls: urls).Readings);

        Assert.Equal(PulsePublishPlanner.SummaryMaxLength, reading.Summary.Length);
        Assert.EndsWith("…", reading.Summary);
        Assert.Equal(40, reading.OpenIssueCount);
    }

    [Fact]
    public void A_code_set_too_long_for_its_column_is_stored_as_a_hash_that_still_tracks_changes()
    {
        var many = Enumerable.Range(1, 60).Select(i => $"UrlMissing:{new string('k', 20)}-{i}").ToList();

        var encoded = PulsePublishPlanner.EncodeCodes(many);

        Assert.True(encoded.Length <= PulsePublishPlanner.CodesMaxLength);
        Assert.StartsWith("sha256:", encoded);
        Assert.NotEqual(encoded, PulsePublishPlanner.EncodeCodes(many.Skip(1)));
        Assert.Equal(encoded, PulsePublishPlanner.EncodeCodes(Enumerable.Reverse(many)));
    }

    [Fact]
    public void The_same_alert_from_two_sources_is_one_issue_at_its_earliest_trigger()
    {
        var early = Now.AddMinutes(-30);
        var plan = Plan(
        [
            AgentHost(Alert("disk-used-percent", "critical", "Disk used is 96%", early)),
            Source("heartbeat", "D1", OverallHealthStatus.Critical, Now.AddSeconds(-5),
                Alert("disk-used-percent", "critical", "Disk used is 96%", Now.AddMinutes(-2))),
        ]);

        var reading = Assert.Single(plan.Readings);
        Assert.Equal(1, reading.OpenIssueCount);
        Assert.Equal(early, reading.OldestTriggeredUtc);
    }

    [Fact]
    public void A_planned_reading_maps_to_the_gateway_row_and_the_item_state()
    {
        var reading = Assert.Single(Plan([AgentHost(Alert("cpu-percent", "critical", "CPU is 99%", Now.AddMinutes(-3)))]).Readings);

        var row = reading.ToPulseReading(PulseDevice, Now);
        Assert.Equal(new PulseReading(PulseDevice, 0, Now, "CPU is 99% (critical)", 1, Now.AddMinutes(-3), 3), row);

        var state = reading.ToItemState(DeviceId, Now);
        Assert.Equal((DeviceId, 0, (byte)3, "cpu-percent/critical", Now),
            (state.DeviceInstanceId, state.MonitorItemId, state.ErrorLevel, state.OpenIssueCodes, state.LastPublishedUtc));
    }

    [Fact]
    public void Outcomes_advance_success_or_count_failures()
    {
        var state = DeviceStateRow();
        state.ConsecutiveFailures = 2;
        state.LastError = "timeout";

        PulsePublishPlanner.RecordOutcome(state, Now, heartbeatWrittenUtc: Now.AddSeconds(-30), error: null);
        Assert.Equal((Now, Now, 0, (string?)null, Now.AddSeconds(-30)),
            (state.LastAttemptUtc, state.LastSuccessUtc, state.ConsecutiveFailures, state.LastError, state.LastHeartbeatSentUtc));

        var later = Now.AddMinutes(1);
        PulsePublishPlanner.RecordOutcome(state, later, heartbeatWrittenUtc: null, error: "Pulse unreachable");
        Assert.Equal((later, Now, 1, "Pulse unreachable", Now.AddSeconds(-30)),
            (state.LastAttemptUtc, state.LastSuccessUtc, state.ConsecutiveFailures, state.LastError, state.LastHeartbeatSentUtc));

        PulsePublishPlanner.RecordOutcome(state, later, heartbeatWrittenUtc: null, error: null, linkBroken: true);
        Assert.Equal(later, state.LinkBrokenUtc);
        Assert.Equal(2, state.ConsecutiveFailures);
    }

    // Builders

    private static PulsePlan Plan(
        IReadOnlyCollection<DeviceSourceState> sources,
        SentinelPulseDeviceState? device = null,
        IReadOnlyCollection<SentinelPulseItemState>? items = null,
        IReadOnlyCollection<PulseUrlMonitor>? urls = null,
        PulseIssueOverrides? overrides = null,
        PulseSourceNames? names = null) =>
        PulsePublishPlanner.Plan(new PulsePlanInput
        {
            DeviceInstanceId = DeviceId,
            PulseDeviceId = PulseDevice,
            PulseCustomerId = Customer,
            Sources = sources,
            DeviceState = device,
            ItemStates = items ?? [],
            UrlMonitors = urls ?? [],
            Overrides = overrides ?? PulseIssueOverrides.None,
            Names = names ?? PulseSourceNames.Empty,
            NowUtc = Now,
        });

    private static DeviceSourceState Healthy() => Source("heartbeat", "D1", OverallHealthStatus.Healthy, Now.AddSeconds(-20));

    private static DeviceSourceState AgentHost(params ActiveAlert[] alerts) =>
        Source("agent-host", "host-7",
            alerts.Any(a => a.Severity == "critical") ? OverallHealthStatus.Critical : OverallHealthStatus.Warning,
            Now.AddSeconds(-20), alerts);

    private static DeviceSourceState Source(string type, string key, OverallHealthStatus health, DateTime lastSeen, params ActiveAlert[] alerts) => new()
    {
        DeviceInstanceId = DeviceId,
        SourceType = type,
        SourceKey = key,
        OverallHealth = (int)health,
        LastSeenUtc = lastSeen,
        CustomerId = Customer,
        LastStateJson = JsonSerializer.Serialize(new DeviceState { OverallHealth = health, ActiveAlerts = [.. alerts] }),
    };

    private static ActiveAlert Alert(string monitorPointId, string severity, string message, DateTime? triggered = null) => new()
    {
        AlertId = monitorPointId,
        MonitorPointId = monitorPointId,
        Severity = severity,
        Message = message,
        TriggeredAt = triggered ?? Now.AddMinutes(-1),
    };

    private static PulseUrlMonitor Url(string monitorId, bool missing, bool monitored = true) =>
        new(monitorId, $"https://player.example/{monitorId}", monitored, missing ? "missing" : "online", Now.AddMinutes(-4));

    private static SentinelPulseDeviceState DeviceStateRow(
        string pulseDeviceId = PulseDevice, string customer = Customer, DateTime? lastHeartbeat = null, DateTime? linkBroken = null) => new()
    {
        DeviceInstanceId = DeviceId,
        PulseDeviceId = pulseDeviceId,
        PulseCustomerId = customer,
        LastHeartbeatSentUtc = lastHeartbeat,
        LinkBrokenUtc = linkBroken,
    };

    private static SentinelPulseItemState ItemState(int item, byte level, string codes, string summary = "", DateTime? published = null) => new()
    {
        DeviceInstanceId = DeviceId,
        MonitorItemId = item,
        ErrorLevel = level,
        OpenIssueCodes = codes,
        Summary = summary,
        LastPublishedUtc = published ?? Now.AddMinutes(-10),
    };

    private static PulseIssueOverrides Overrides(params SentinelPulseIssueLevel[] rows) => new(rows);

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
