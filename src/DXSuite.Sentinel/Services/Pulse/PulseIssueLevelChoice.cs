using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>
/// One Pulse Reporting tab row as its two select values. <see cref="Level"/> is empty to inherit, "1" to "4",
/// or <see cref="DoNotReportValue"/>; <see cref="Item"/> is empty to inherit or a Pulse MonitorItemID.
/// </summary>
public sealed record PulseIssueLevelChoice(string Level, string Item)
{
    public const string Inherit = "";
    public const string DoNotReportValue = "dnr";

    public static readonly PulseIssueLevelChoice Default = new(Inherit, Inherit);

    public static PulseIssueLevelChoice FromRow(SentinelPulseIssueLevel? row) => row is null
        ? Default
        : new(row.DoNotReport ? DoNotReportValue : row.ErrorLevel?.ToString() ?? Inherit,
              row.MonitorItemId?.ToString() ?? Inherit);

    public bool DoNotReport => Level == DoNotReportValue;

    public byte? ErrorLevel => byte.TryParse(Level, out var level) ? level : null;

    public int? MonitorItemId => int.TryParse(Item, out var item) ? item : null;

    public bool IsDefault => ErrorLevel is null && !DoNotReport && MonitorItemId is null;

    /// <summary>The override row these values would save, or null when everything inherits and no row is kept.</summary>
    public SentinelPulseIssueLevel? ToRow(PulseIssueKey key) => IsDefault
        ? null
        : new SentinelPulseIssueLevel
        {
            Kind = key.Kind,
            MonitorPointId = key.MonitorPointId,
            Severity = key.Severity,
            DirectorAlertKind = key.DirectorAlertKind,
            ErrorLevel = ErrorLevel,
            DoNotReport = DoNotReport,
            MonitorItemId = MonitorItemId,
        };
}

/// <summary>Pulse vocabulary the Pulse Reporting tab shows beside the raw numbers.</summary>
public static class PulseMonitorItems
{
    /// <summary>
    /// Items the Pulse agents write. A live agent's newer reading masks Sentinel's on the same item, because
    /// Pulse shows the latest reading per (device, item).
    /// </summary>
    public static readonly IReadOnlySet<int> AgentItemIds =
        new HashSet<int>([1, 2, 3, 4, 10, .. Enumerable.Range(13, 13)]);

    public static string LevelName(byte level) => level switch
    {
        1 => "Information",
        2 => "Warning",
        3 => "Error",
        4 => "Mission Critical",
        _ => level.ToString(),
    };

    /// <summary>
    /// The chosen items Pulse does not have. Saving one would fail every publish cycle on
    /// <c>FK_tblPulse_tblMonitoritem</c>, so the tab refuses the save.
    /// </summary>
    public static IReadOnlyList<PulseIssueKey> MissingItems(
        IEnumerable<KeyValuePair<PulseIssueKey, PulseIssueLevelChoice>> choices, IReadOnlySet<int> pulseItemIds) =>
        choices
            .Where(c => c.Value.MonitorItemId is { } item && !pulseItemIds.Contains(item))
            .Select(c => c.Key)
            .ToList();
}
