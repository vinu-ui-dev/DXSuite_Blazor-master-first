using DXSuite.Sentinel.Components;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services;
using DXSuite.Sentinel.Services.Pulse;

namespace DXSuite.Sentinel.Tests;

/// <summary>
/// Pins the Monitored Devices rules from docs/spec/sentinel-pulse-reporting.md section 6: which Pulse devices
/// are suggested, the DeviceID Create in Pulse proposes, whether Monitor may be offered, and the status chip.
/// </summary>
public class PulseLinkRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlySet<string> NoneLinked = new HashSet<string>();

    // Suggestions

    [Fact]
    public void Suggests_only_strong_matches_case_insensitively()
    {
        var device = Device("HD1025 Bench", director: "m2e357004935");
        var pulse = new[]
        {
            Pulse("M2E357004935"),
            Pulse("hd1025 bench"),
            Pulse("BENCH-01", description: "HD1025 BENCH"),
            Pulse("HD1025", description: "HD1025 bench player"),
        };

        var suggestions = PulseLinkService.Suggestions(device, pulse, NoneLinked);

        Assert.Equal(["M2E357004935", "hd1025 bench", "BENCH-01"], suggestions.Select(s => s.Device.DeviceId));
        Assert.Equal(
            [PulseSuggestionReason.DirectorIdIsDeviceId, PulseSuggestionReason.NameIsDeviceId, PulseSuggestionReason.NameIsDescription],
            suggestions.Select(s => s.Reason));
    }

    [Fact]
    public void A_Pulse_device_linked_to_another_Sentinel_device_is_not_suggested()
    {
        var device = Device("LOBBY-01");

        var suggestions = PulseLinkService.Suggestions(device, [Pulse("LOBBY-01")], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lobby-01" });

        Assert.Empty(suggestions);
    }

    // Default DeviceID

    [Fact]
    public void Default_DeviceID_is_the_Director_id_when_it_is_not_a_GUID()
    {
        Assert.Equal("42D98X001975", PulseLinkService.DefaultDeviceId(Device("LS424 Bench", director: "42D98X001975")));
    }

    [Theory]
    [InlineData("cloud-3-653d101e-32ea-41d7-91f4-f4a640f7f5d0")]
    [InlineData("653D101E-32EA-41D7-91F4-F4A640F7F5D0")]
    [InlineData(null)]
    [InlineData("  ")]
    public void Default_DeviceID_slugs_the_display_name_when_the_Director_id_is_a_GUID_or_absent(string? director)
    {
        Assert.Equal("DXM-VIDEO-WALL", PulseLinkService.DefaultDeviceId(Device("DXM Video Wall", director)));
    }

    [Theory]
    [InlineData("NR Laptop25 - ID 1", "NR-LAPTOP25-ID-1")]
    [InlineData("  Ian Radiant Test (wall) ", "IAN-RADIANT-TEST-WALL")]
    [InlineData("---", "")]
    public void Slug_keeps_letters_digits_and_single_hyphens(string name, string expected)
    {
        Assert.Equal(expected, PulseLinkService.Slug(name));
    }

    [Fact]
    public void Slug_is_capped_at_the_DeviceID_column_without_a_trailing_hyphen()
    {
        var slug = PulseLinkService.Slug(new string('A', 49) + " B" + new string('C', 20));

        Assert.Equal(new string('A', 49), slug);
        Assert.True(slug.Length <= PulseGateway.MaxDeviceIdLength);
    }

    // Monitor eligibility uses the planner's rule

    [Fact]
    public void Heartbeat_eligibility_is_the_planners_rule()
    {
        var fresh = Source(OverallHealthStatus.Warning, Now.AddSeconds(-30));
        var stale = Source(OverallHealthStatus.Healthy, Now.AddMinutes(-6));
        var dead = Source(OverallHealthStatus.Healthy, Now.AddMinutes(-20));

        Assert.Equal((Now.AddSeconds(-30), (string?)null), PulsePublishPlanner.HeartbeatEligibility([fresh], Now));
        Assert.Equal(((DateTime?)null, PulsePublishPlanner.SuppressedStaleEvidence), PulsePublishPlanner.HeartbeatEligibility([stale], Now));
        Assert.Equal(((DateTime?)null, PulsePublishPlanner.SuppressedOffline), PulsePublishPlanner.HeartbeatEligibility([dead], Now));
        Assert.Equal(((DateTime?)null, PulsePublishPlanner.SuppressedUnknown), PulsePublishPlanner.HeartbeatEligibility([], Now));
    }

    // Start and Stop monitoring

    [Fact]
    public void Start_monitoring_on_a_healthy_device_writes_its_eligible_heartbeat()
    {
        var change = PulseLinkService.PlanMonitorChange(true, [Source(OverallHealthStatus.Healthy, Now.AddSeconds(-30))], Now);

        Assert.Equal(new PulseMonitorChange(true, Now.AddSeconds(-30), null), change);
    }

    [Fact]
    public void Start_monitoring_is_refused_on_a_dead_device()
    {
        var change = PulseLinkService.PlanMonitorChange(true, [Source(OverallHealthStatus.Healthy, Now.AddMinutes(-20))], Now);

        Assert.Equal(new PulseMonitorChange(false, null, PulsePublishPlanner.SuppressedOffline), change);
    }

    [Fact]
    public void Start_monitoring_is_refused_on_stale_evidence()
    {
        var change = PulseLinkService.PlanMonitorChange(true, [Source(OverallHealthStatus.Healthy, Now.AddMinutes(-6))], Now);

        Assert.Equal(new PulseMonitorChange(false, null, PulsePublishPlanner.SuppressedStaleEvidence), change);
    }

    [Fact]
    public void Start_monitoring_is_refused_when_health_is_Unknown()
    {
        var change = PulseLinkService.PlanMonitorChange(true, [], Now);

        Assert.Equal(new PulseMonitorChange(false, null, PulsePublishPlanner.SuppressedUnknown), change);
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(-6)]
    [InlineData(null)]
    public void Stop_monitoring_needs_no_eligibility_and_writes_no_heartbeat(int? lastSeenMinutes)
    {
        DeviceSourceState[] sources = lastSeenMinutes is { } m ? [Source(OverallHealthStatus.Healthy, Now.AddMinutes(m))] : [];

        Assert.Equal(new PulseMonitorChange(true, null, null), PulseLinkService.PlanMonitorChange(false, sources, Now));
    }

    // Status chip

    [Fact]
    public void Chip_follows_the_spec_table_in_order()
    {
        var zone = ViewerTimeZone.Utc;
        var devices = new Dictionary<string, PulseDevice>(StringComparer.OrdinalIgnoreCase)
        {
            ["UP"] = Pulse("UP", heartbeat: Now.AddMinutes(-2)),
            ["DOWN"] = Pulse("DOWN", heartbeat: Now.AddMinutes(-7)),
            ["NEVER"] = Pulse("NEVER"),
            ["RETIRED"] = Pulse("RETIRED", monitor: 0),
            ["SNOOZED"] = Pulse("SNOOZED", snoozeUntil: Now.AddHours(2)),
        };
        var suppressed = new SentinelPulseDeviceState { SuppressionReason = PulsePublishPlanner.SuppressedOffline };
        var broken = new SentinelPulseDeviceState { LinkBrokenUtc = Now };

        PulseChipKind Kind(string? id, SentinelPulseDeviceState? state = null, IReadOnlyDictionary<string, PulseDevice>? pulse = null) =>
            PulseStatusChip.For(id, state, pulse ?? devices, Now, zone).Kind;

        Assert.Equal(PulseChipKind.NotLinked, Kind(null));
        Assert.Equal(PulseChipKind.LinkBroken, Kind("UP", broken));
        Assert.Equal(PulseChipKind.PulseUnavailable, PulseStatusChip.For("UP", null, null, Now, zone).Kind);
        Assert.Equal(PulseChipKind.MissingInPulse, Kind("GONE"));
        Assert.Equal(PulseChipKind.NotMonitored, Kind("RETIRED", suppressed));
        Assert.Equal(PulseChipKind.Snoozed, Kind("SNOOZED", suppressed));
        Assert.Equal(PulseChipKind.DownCannotVouch, Kind("DOWN", suppressed));
        Assert.Equal(PulseChipKind.Down, Kind("DOWN"));
        Assert.Equal(PulseChipKind.Down, Kind("NEVER"));
        Assert.Equal(PulseChipKind.Up, Kind("UP", suppressed));
        var label = PulseStatusChip.For("SNOOZED", null, devices, Now, zone).Label;
        Assert.StartsWith("Snoozed until ", label);
        Assert.EndsWith("2:00 PM", label);
    }

    private static DeviceInstance Device(string name, string? director = null) => new()
    {
        DeviceInstanceId = 7,
        DisplayName = name,
        DirectorDeviceId = director,
        CustomerId = "dev",
        IsEnabled = true,
    };

    private static PulseDevice Pulse(string id, string? description = null, int monitor = 1, DateTime? heartbeat = null, DateTime? snoozeUntil = null) =>
        new(id, description, monitor, snoozeUntil is not null, snoozeUntil, heartbeat, []);

    private static DeviceSourceState Source(OverallHealthStatus health, DateTime lastSeen) => new()
    {
        DeviceInstanceId = 7,
        SourceType = "heartbeat",
        SourceKey = "D1",
        OverallHealth = (int)health,
        LastSeenUtc = lastSeen,
        CustomerId = "dev",
    };
}
