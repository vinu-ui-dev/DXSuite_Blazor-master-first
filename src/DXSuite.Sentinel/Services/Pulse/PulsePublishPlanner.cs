using System.Security.Cryptography;
using System.Text;
using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>A Director URL monitor whose reporting device is the device being planned.</summary>
public sealed record PulseUrlMonitor(string MonitorId, string UrlKey, bool IsMonitored, string AlertState, DateTime? LastTransitionUtc);

/// <summary>A cloud connector as a Source offline summary names it, e.g. Pixera 'SLC Ticket Wall'.</summary>
public sealed record PulseConnectorName(string Platform, string DisplayName);

/// <summary>Names the Source offline summaries need, keyed by the ids embedded in each SourceKey.</summary>
public sealed record PulseSourceNames(
    IReadOnlyDictionary<int, PulseConnectorName> Connectors,
    IReadOnlyDictionary<int, string> Gateways)
{
    public static readonly PulseSourceNames Empty = new(new Dictionary<int, PulseConnectorName>(), new Dictionary<int, string>());
}

/// <summary>Everything the planner decides from for one linked device in one cycle.</summary>
public sealed record PulsePlanInput
{
    public required int DeviceInstanceId { get; init; }
    public required string PulseDeviceId { get; init; }
    public required string PulseCustomerId { get; init; }
    public required IReadOnlyCollection<DeviceSourceState> Sources { get; init; }
    public IReadOnlyCollection<PulseUrlMonitor> UrlMonitors { get; init; } = [];
    public SentinelPulseDeviceState? DeviceState { get; init; }
    public IReadOnlyCollection<SentinelPulseItemState> ItemStates { get; init; } = [];
    public PulseIssueOverrides Overrides { get; init; } = PulseIssueOverrides.None;
    public PulseSourceNames Names { get; init; } = PulseSourceNames.Empty;
    public TimeSpan ReassertInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>The cycle stamp, already truncated to the second.</summary>
    public required DateTime NowUtc { get; init; }
}

public enum PulseReadingReason
{
    /// <summary>(ErrorLevel, sorted open issue codes) differs from what was last written, or nothing was.</summary>
    Changed,

    /// <summary>An open item re-written unchanged so Pulse's nightly prune cannot remove it.</summary>
    Reassert,
}

/// <summary>One <c>tblPulse</c> row the publisher intends to write, and the item state it records on success.</summary>
public sealed record PlannedReading(
    int MonitorItemId,
    byte ErrorLevel,
    string Summary,
    int OpenIssueCount,
    DateTime? OldestTriggeredUtc,
    string OpenIssueCodes,
    PulseReadingReason Reason)
{
    public bool IsClear => ErrorLevel == 0;

    public PulseReading ToPulseReading(string pulseDeviceId, DateTime stampUtc) =>
        new(pulseDeviceId, MonitorItemId, stampUtc, Summary, OpenIssueCount, OldestTriggeredUtc, ErrorLevel);

    public SentinelPulseItemState ToItemState(int deviceInstanceId, DateTime publishedUtc) => new()
    {
        DeviceInstanceId = deviceInstanceId,
        MonitorItemId = MonitorItemId,
        ErrorLevel = ErrorLevel,
        OpenIssueCodes = OpenIssueCodes,
        Summary = Summary,
        LastPublishedUtc = publishedUtc,
    };
}

/// <summary>An open issue with where it resolved to report.</summary>
public sealed record ResolvedIssue(OpenIssue Issue, byte ErrorLevel, int MonitorItemId);

/// <summary>What the publisher should do for one device this cycle. The planner has done no I/O.</summary>
public sealed record PulsePlan
{
    /// <summary>The device is marked link broken: attempt nothing until the operator retries or relinks.</summary>
    public bool LinkBroken { get; init; }

    /// <summary>The stored state named another Pulse device or customer: delete its item state rows first.</summary>
    public bool Relinked { get; init; }

    /// <summary>True when Sentinel can vouch for the device; <see cref="EligibleHeartbeatUtc"/> is then set.</summary>
    public bool HeartbeatEligible { get; init; }

    /// <summary>The evidence timestamp a heartbeat would carry, whether or not it is written this cycle.</summary>
    public DateTime? EligibleHeartbeatUtc { get; init; }

    /// <summary>The <c>tblDevice.Heartbeat</c> to write this cycle, or null to write none.</summary>
    public DateTime? HeartbeatUtc { get; init; }

    /// <summary><c>Offline</c>, <c>Unknown</c> or <c>Stale evidence</c> when no heartbeat is eligible.</summary>
    public string? SuppressionReason { get; init; }

    /// <summary>Open issues that report, after Do not report is dropped.</summary>
    public IReadOnlyList<ResolvedIssue> Issues { get; init; } = [];

    /// <summary>Readings to write after the heartbeat, in monitor item order.</summary>
    public IReadOnlyList<PlannedReading> Readings { get; init; } = [];

    /// <summary>The device state row with this cycle's link and suppression applied, before any outcome.</summary>
    public SentinelPulseDeviceState? DeviceState { get; init; }

    public bool HasWrites => HeartbeatUtc is not null || Readings.Count > 0;
}

/// <summary>
/// Decides, for one linked device and one cycle, what the Pulse publisher writes: whether a heartbeat is
/// eligible and its value, the open issues, and a reading per monitor item whose (ErrorLevel, sorted codes)
/// changed or whose open state is due for a retention re-assert. Pure: no database, no clock. The engine
/// performs the writes and records each outcome with <see cref="RecordOutcome"/>.
/// </summary>
public static class PulsePublishPlanner
{
    /// <summary>
    /// Newest fresh evidence older than this suppresses the heartbeat. Inside Pulse's own 6 minute down
    /// threshold so Pulse does the detecting, and far tighter than the 10 minute staleness window.
    /// </summary>
    public static readonly TimeSpan HeartbeatEvidenceWindow = TimeSpan.FromMinutes(5);

    public const string SuppressedOffline = "Offline";
    public const string SuppressedUnknown = "Unknown";
    public const string SuppressedStaleEvidence = "Stale evidence";

    public const int SummaryMaxLength = 500;
    public const int CodesMaxLength = 1000;
    public const string ClearSummary = "OK";

    private const string CodeSeparator = ";";
    private const string SummarySeparator = "; ";

    public static PulsePlan Plan(PulsePlanInput input)
    {
        var now = input.NowUtc;
        var stored = input.DeviceState;

        // A changed link discards everything recorded against the old Pulse device and starts as a
        // first run. Nothing is written to the old device.
        var relinked = stored is not null
            && (!string.Equals(stored.PulseDeviceId, input.PulseDeviceId, StringComparison.Ordinal)
                || !string.Equals(stored.PulseCustomerId, input.PulseCustomerId, StringComparison.Ordinal));
        var current = stored is null || relinked ? null : stored;

        if (current?.LinkBrokenUtc is not null)
            return new PulsePlan { LinkBroken = true, DeviceState = Copy(current) };

        var state = DeviceHealthRollup.BuildState(input.Sources, now);
        var fresh = input.Sources.Where(s => now - s.LastSeenUtc <= DeviceHealthRollup.StalenessWindow).ToList();

        var (eligibleHeartbeat, suppression) = HeartbeatEligibility(state.OverallHealth, fresh, now);
        var heartbeatToWrite = eligibleHeartbeat is { } hb && hb != current?.LastHeartbeatSentUtc ? eligibleHeartbeat : null;

        var issues = DeriveIssues(input, state, fresh, eligibleHeartbeat is not null);
        var resolved = Resolve(issues, input.Overrides);
        var itemStates = relinked
            ? new Dictionary<int, SentinelPulseItemState>()
            : input.ItemStates.ToDictionary(s => s.MonitorItemId);

        var nextState = current is null
            ? new SentinelPulseDeviceState { DeviceInstanceId = input.DeviceInstanceId }
            : Copy(current);
        nextState.PulseDeviceId = input.PulseDeviceId;
        nextState.PulseCustomerId = input.PulseCustomerId;
        nextState.SuppressionReason = suppression;

        return new PulsePlan
        {
            Relinked = relinked,
            HeartbeatEligible = eligibleHeartbeat is not null,
            EligibleHeartbeatUtc = eligibleHeartbeat,
            HeartbeatUtc = heartbeatToWrite,
            SuppressionReason = suppression,
            Issues = resolved,
            Readings = PlanReadings(resolved, itemStates, input.ReassertInterval, now),
            DeviceState = nextState,
        };
    }

    /// <summary>
    /// Records one cycle's outcome on the device state: the attempt always, success or the failure count and
    /// error, the heartbeat only when it landed, and the link broken mark on a dangling link.
    /// </summary>
    public static void RecordOutcome(
        SentinelPulseDeviceState state, DateTime nowUtc, DateTime? heartbeatWrittenUtc, string? error, bool linkBroken = false)
    {
        state.LastAttemptUtc = nowUtc;
        if (heartbeatWrittenUtc is not null) state.LastHeartbeatSentUtc = heartbeatWrittenUtc;
        if (linkBroken) state.LinkBrokenUtc = nowUtc;

        if (error is null && !linkBroken)
        {
            state.LastSuccessUtc = nowUtc;
            state.ConsecutiveFailures = 0;
            state.LastError = null;
        }
        else
        {
            state.ConsecutiveFailures++;
            state.LastError = Truncate(error ?? "Pulse device is missing.", 500);
        }
    }

    /// <summary>
    /// Whether Sentinel can vouch for a device now: the heartbeat it would write, or why none is written.
    /// Create in Pulse offers Monitor only when this yields a heartbeat, so the two can never disagree.
    /// </summary>
    public static (DateTime? HeartbeatUtc, string? SuppressionReason) HeartbeatEligibility(
        IReadOnlyCollection<DeviceSourceState> sources, DateTime nowUtc)
    {
        var fresh = sources.Where(s => nowUtc - s.LastSeenUtc <= DeviceHealthRollup.StalenessWindow).ToList();
        return HeartbeatEligibility(DeviceHealthRollup.Roll(sources, nowUtc), fresh, nowUtc);
    }

    private static (DateTime? Heartbeat, string? Suppression) HeartbeatEligibility(
        OverallHealthStatus health, IReadOnlyCollection<DeviceSourceState> fresh, DateTime now)
    {
        switch (health)
        {
            case OverallHealthStatus.Offline: return (null, SuppressedOffline);
            case OverallHealthStatus.Unknown: return (null, SuppressedUnknown);
        }

        if (fresh.Count == 0) return (null, SuppressedStaleEvidence);
        var newest = fresh.Max(s => s.LastSeenUtc);
        if (now - newest > HeartbeatEvidenceWindow) return (null, SuppressedStaleEvidence);

        // A source clock ahead of ours must not put Pulse's heartbeat in the future.
        return (newest > now ? now : newest, null);
    }

    private static List<OpenIssue> DeriveIssues(
        PulsePlanInput input, DeviceState state, IReadOnlyCollection<DeviceSourceState> fresh, bool heartbeatEligible)
    {
        var issues = new List<OpenIssue>();

        issues.AddRange(state.ActiveAlerts
            .Where(a => !string.IsNullOrWhiteSpace(a.MonitorPointId))
            .Select(OpenIssue.FromAlert));

        // A stale source on a device whose heartbeat has stopped is already Pulse's down alert.
        if (heartbeatEligible)
        {
            var freshTypes = fresh.Select(s => s.SourceType).ToHashSet(StringComparer.Ordinal);
            issues.AddRange(input.Sources
                .Where(s => !freshTypes.Contains(s.SourceType))
                .GroupBy(s => s.SourceType, StringComparer.Ordinal)
                .Select(g => g.MaxBy(s => s.LastSeenUtc)!)
                .Select(s => OpenIssue.SourceOffline(
                    s.SourceType,
                    SourceOfflineSummary(s, input.Names),
                    s.LastSeenUtc + DeviceHealthRollup.StalenessWindow)));
        }

        issues.AddRange(input.UrlMonitors
            .Where(u => u.IsMonitored && string.Equals(u.AlertState, "missing", StringComparison.OrdinalIgnoreCase))
            .Select(u => OpenIssue.UrlMissing(u.MonitorId, u.UrlKey, u.LastTransitionUtc)));

        // Two sources can raise the same alert; one code is one issue, kept at its earliest trigger.
        return issues
            .GroupBy(i => i.Code, StringComparer.Ordinal)
            .Select(g => g.OrderBy(i => i.TriggeredAtUtc ?? DateTime.MaxValue).First())
            .ToList();
    }

    private static List<ResolvedIssue> Resolve(IEnumerable<OpenIssue> issues, PulseIssueOverrides overrides)
    {
        var resolved = new List<ResolvedIssue>();
        foreach (var issue in issues)
        {
            var r = PulseIssueLevelService.Resolve(issue.Key, overrides);
            if (r.ErrorLevel is { } level) resolved.Add(new ResolvedIssue(issue, level, r.MonitorItemId));
        }
        return resolved;
    }

    private static List<PlannedReading> PlanReadings(
        IReadOnlyCollection<ResolvedIssue> issues,
        IReadOnlyDictionary<int, SentinelPulseItemState> itemStates,
        TimeSpan reassertInterval,
        DateTime now)
    {
        var byItem = issues.GroupBy(i => i.MonitorItemId).ToDictionary(g => g.Key, g => g.ToList());
        var readings = new List<PlannedReading>();

        foreach (var item in byItem.Keys.Union(itemStates.Keys).Order())
        {
            var open = byItem.GetValueOrDefault(item) ?? [];
            itemStates.TryGetValue(item, out var stored);

            // First run and new items publish only what is open: no '0' baseline Pulse never held.
            if (stored is null && open.Count == 0) continue;

            var level = open.Count == 0 ? (byte)0 : open.Max(i => i.ErrorLevel);
            var codes = EncodeCodes(open.Select(i => i.Issue.Code));

            PulseReadingReason reason;
            if (stored is null || stored.ErrorLevel != level || !string.Equals(stored.OpenIssueCodes, codes, StringComparison.Ordinal))
                reason = PulseReadingReason.Changed;
            else if (level > 0 && now - stored.LastPublishedUtc > reassertInterval)
                reason = PulseReadingReason.Reassert;
            else
                continue;

            readings.Add(new PlannedReading(
                item,
                level,
                open.Count == 0 ? ClearSummary : Summarize(open),
                open.Count,
                open.Count == 0 ? null : open.Min(i => i.Issue.TriggeredAtUtc),
                codes,
                reason));
        }

        return readings;
    }

    /// <summary>
    /// The sorted codes as stored in <c>SentinelPulseItemState.OpenIssueCodes</c>. A set too long for the
    /// column is stored as its hash, which still changes whenever the set does.
    /// </summary>
    public static string EncodeCodes(IEnumerable<string> codes)
    {
        var joined = string.Join(CodeSeparator, codes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        if (joined.Length <= CodesMaxLength) return joined;
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static string Summarize(IEnumerable<ResolvedIssue> open) =>
        Truncate(
            string.Join(SummarySeparator, open
                .OrderByDescending(i => i.ErrorLevel)
                .ThenBy(i => i.Issue.Code, StringComparer.Ordinal)
                .Select(i => i.Issue.Summary)),
            SummaryMaxLength);

    private static string SourceOfflineSummary(DeviceSourceState source, PulseSourceNames names)
    {
        var key = source.SourceKey;
        switch (source.SourceType)
        {
            case DeviceIdentity.SourceCloudConnector:
                var connectorId = key.Split(':', 2)[0];
                return int.TryParse(connectorId, out var cid) && names.Connectors.TryGetValue(cid, out var connector)
                    ? $"Source offline: {connector.Platform} '{connector.DisplayName}'"
                    : $"Source offline: connector {connectorId}";
            case DeviceIdentity.SourceGateway:
                if (string.Equals(key, "direct", StringComparison.OrdinalIgnoreCase)) return "Source offline: direct probe";
                return int.TryParse(key, out var gid) && names.Gateways.TryGetValue(gid, out var gateway)
                    ? $"Source offline: gateway '{gateway}'"
                    : $"Source offline: gateway {key}";
            case DeviceIdentity.SourceHeartbeat:
                return $"Source offline: Engage heartbeat ({key})";
            case DeviceIdentity.SourceAgentHost:
                return $"Source offline: agent ({key})";
            default:
                return $"Source offline: {source.SourceType} ({key})";
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static SentinelPulseDeviceState Copy(SentinelPulseDeviceState s) => new()
    {
        DeviceInstanceId = s.DeviceInstanceId,
        PulseCustomerId = s.PulseCustomerId,
        PulseDeviceId = s.PulseDeviceId,
        LastHeartbeatSentUtc = s.LastHeartbeatSentUtc,
        LastAttemptUtc = s.LastAttemptUtc,
        LastSuccessUtc = s.LastSuccessUtc,
        ConsecutiveFailures = s.ConsecutiveFailures,
        SuppressionReason = s.SuppressionReason,
        LastError = s.LastError,
        LinkBrokenUtc = s.LinkBrokenUtc,
    };
}
