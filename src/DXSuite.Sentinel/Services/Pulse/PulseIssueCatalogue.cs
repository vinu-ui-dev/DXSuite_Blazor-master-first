namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>One issue type as shipped: its config tab group and label, and the ErrorLevel it reports at.</summary>
public sealed record PulseCatalogueEntry(string Group, string Label, PulseIssueKey Key, byte ShippedLevel);

/// <summary>
/// The shipped ErrorLevel of every issue type. Overrides live in <c>SentinelPulseIssueLevel</c>; these values
/// live in code so "Reset to default" is a row delete. A monitor point missing from this list still reports
/// through its severity default, so a new point needs no change here. Monitor points mirror the alerts raised
/// by <c>EngageTelemetryParser</c> and <c>AgentHostRegistry</c>.
/// </summary>
public static class PulseIssueCatalogue
{
    public const string GroupDefaults = "Defaults";
    public const string GroupEngage = "Engage player";
    public const string GroupAgentHost = "Agent host";
    public const string GroupDirector = "Director";
    public const string GroupSourceOffline = "Source offline";

    /// <summary>Every issue type reads from item 0 (Device) unless an override moves it.</summary>
    public const int DefaultMonitorItemId = 0;

    private const byte WarningLevel = 2;
    private const byte CriticalLevel = 3;

    public static readonly IReadOnlyList<PulseCatalogueEntry> Entries =
    [
        new(GroupDefaults, "Severity default, Warning", PulseIssueKey.SeverityDefault(PulseIssueKey.Warning), WarningLevel),
        new(GroupDefaults, "Severity default, Critical", PulseIssueKey.SeverityDefault(PulseIssueKey.Critical), CriticalLevel),

        new(GroupEngage, "Web content status", PulseIssueKey.MonitorPoint("web-content-status", PulseIssueKey.Warning), WarningLevel),
        new(GroupEngage, "Web content status", PulseIssueKey.MonitorPoint("web-content-status", PulseIssueKey.Critical), CriticalLevel),
        new(GroupEngage, "Web content JS errors", PulseIssueKey.MonitorPoint("web-content-js-errors", PulseIssueKey.Warning), WarningLevel),
        new(GroupEngage, "Display input", PulseIssueKey.MonitorPoint("input-status", PulseIssueKey.Warning), WarningLevel),
        new(GroupEngage, "Console errors", PulseIssueKey.MonitorPoint("console-errors", PulseIssueKey.Warning), WarningLevel),
        new(GroupEngage, "Screenshot age", PulseIssueKey.MonitorPoint("screenshot-age", PulseIssueKey.Warning), WarningLevel),

        new(GroupAgentHost, "CPU", PulseIssueKey.MonitorPoint("cpu-percent", PulseIssueKey.Warning), WarningLevel),
        new(GroupAgentHost, "CPU", PulseIssueKey.MonitorPoint("cpu-percent", PulseIssueKey.Critical), CriticalLevel),
        new(GroupAgentHost, "Memory used", PulseIssueKey.MonitorPoint("memory-used-percent", PulseIssueKey.Warning), WarningLevel),
        new(GroupAgentHost, "Memory used", PulseIssueKey.MonitorPoint("memory-used-percent", PulseIssueKey.Critical), CriticalLevel),
        new(GroupAgentHost, "Disk used", PulseIssueKey.MonitorPoint("disk-used-percent", PulseIssueKey.Warning), WarningLevel),
        new(GroupAgentHost, "Disk used", PulseIssueKey.MonitorPoint("disk-used-percent", PulseIssueKey.Critical), CriticalLevel),

        new(GroupDirector, "URL missing", PulseIssueKey.DirectorUrlMissing, CriticalLevel),

        new(GroupSourceOffline, "Source offline", PulseIssueKey.SourceOffline, WarningLevel),
    ];

    private static readonly Dictionary<PulseIssueKey, PulseCatalogueEntry> ByKey = Entries.ToDictionary(e => e.Key);

    public static PulseCatalogueEntry? Find(PulseIssueKey key) => ByKey.GetValueOrDefault(key);

    /// <summary>
    /// The level an issue type reports at with no overrides: its own entry, else its severity default.
    /// </summary>
    public static byte ShippedLevel(PulseIssueKey key)
    {
        if (ByKey.TryGetValue(key, out var entry)) return entry.ShippedLevel;
        var severity = PulseIssueKey.NormalizeSeverity(key.Severity);
        return ByKey[PulseIssueKey.SeverityDefault(severity)].ShippedLevel;
    }
}
