namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>The <c>SentinelPulseIssueLevel.Kind</c> values: what an ErrorLevel override is configured against.</summary>
public static class PulseIssueKind
{
    public const string SeverityDefault = "SeverityDefault";
    public const string MonitorPoint = "MonitorPoint";
    public const string SourceOffline = "SourceOffline";
    public const string DirectorAlert = "DirectorAlert";
}

/// <summary>
/// Identifies one issue type for ErrorLevel and monitor item lookup, shaped like the unique index on
/// <c>SentinelPulseIssueLevel</c> so an override row and a catalogue entry compare directly.
/// </summary>
public sealed record PulseIssueKey(string Kind, string? MonitorPointId = null, string? Severity = null, string? DirectorAlertKind = null)
{
    public const string Warning = "warning";
    public const string Critical = "critical";
    public const string UrlMissing = "UrlMissing";

    public static PulseIssueKey SeverityDefault(string severity) => new(PulseIssueKind.SeverityDefault, Severity: severity);
    public static PulseIssueKey MonitorPoint(string monitorPointId, string severity) => new(PulseIssueKind.MonitorPoint, monitorPointId, severity);
    public static readonly PulseIssueKey SourceOffline = new(PulseIssueKind.SourceOffline);
    public static readonly PulseIssueKey DirectorUrlMissing = new(PulseIssueKind.DirectorAlert, DirectorAlertKind: UrlMissing);

    /// <summary>Producers only emit warning and critical; anything else is reported as a warning.</summary>
    public static string NormalizeSeverity(string? severity) =>
        string.Equals(severity, Critical, StringComparison.OrdinalIgnoreCase) ? Critical : Warning;
}

/// <summary>
/// One thing currently wrong with a linked device, derived fresh every publisher cycle. <see cref="Code"/>
/// is what change detection compares; <see cref="Key"/> is what the ErrorLevel and monitor item resolve
/// from, with the code's qualifier stripped so the config tab holds one row per kind.
/// </summary>
public sealed record OpenIssue(string Code, PulseIssueKey Key, string Summary, DateTime? TriggeredAtUtc)
{
    public const string SourceOfflinePrefix = "SourceOffline";
    public const string UrlMissingPrefix = "UrlMissing";

    public string LookupKind => Key.Kind;
    public string? MonitorPointId => Key.MonitorPointId;
    public string? Severity => Key.Severity;

    /// <summary><c>{MonitorPointId}/{severity}</c>, e.g. <c>cpu-percent/critical</c>.</summary>
    public static OpenIssue FromAlert(ActiveAlert alert)
    {
        var severity = PulseIssueKey.NormalizeSeverity(alert.Severity);
        return new OpenIssue(
            $"{alert.MonitorPointId}/{severity}",
            PulseIssueKey.MonitorPoint(alert.MonitorPointId, severity),
            $"{alert.Message} ({severity})",
            alert.TriggeredAt);
    }

    /// <summary>
    /// <c>SourceOffline:{SourceType}</c>. Qualified by type rather than SourceKey: a device holds at most one
    /// angle per type, and the full key would churn the code whenever a device moves between gateways.
    /// </summary>
    public static OpenIssue SourceOffline(string sourceType, string summary, DateTime? triggeredAtUtc) =>
        new($"{SourceOfflinePrefix}:{sourceType}", PulseIssueKey.SourceOffline, summary, triggeredAtUtc);

    /// <summary><c>UrlMissing:{MonitorId}</c>, so a second URL dropping on the same device changes the code set.</summary>
    public static OpenIssue UrlMissing(string monitorId, string urlKey, DateTime? triggeredAtUtc) =>
        new($"{UrlMissingPrefix}:{monitorId}", PulseIssueKey.DirectorUrlMissing, $"URL missing: {urlKey}", triggeredAtUtc);

    /// <summary>The code without its <c>:qualifier</c>: <c>UrlMissing:kiosk-3</c> becomes <c>UrlMissing</c>.</summary>
    public static string StripQualifier(string code)
    {
        var colon = code.IndexOf(':');
        return colon < 0 ? code : code[..colon];
    }
}
