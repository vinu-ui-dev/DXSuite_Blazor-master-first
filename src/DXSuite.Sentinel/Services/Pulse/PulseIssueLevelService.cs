using DXSuite.Sentinel.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>
/// Where an issue type reports: its ErrorLevel (null means Do not report) and the Pulse monitor item the
/// reading is filed under. The two flags say whether each came from an override row, for the config tab.
/// </summary>
public sealed record PulseIssueResolution(byte? ErrorLevel, int MonitorItemId, bool LevelOverridden, bool ItemOverridden)
{
    public bool IsReported => ErrorLevel is not null;
}

/// <summary>A tenant's override rows indexed by issue key, ready for <see cref="PulseIssueLevelService.Resolve"/>.</summary>
public sealed class PulseIssueOverrides
{
    public static readonly PulseIssueOverrides None = new([]);

    private readonly Dictionary<PulseIssueKey, SentinelPulseIssueLevel> _rows;

    public PulseIssueOverrides(IEnumerable<SentinelPulseIssueLevel> rows)
    {
        _rows = new Dictionary<PulseIssueKey, SentinelPulseIssueLevel>();
        foreach (var row in rows) _rows[PulseIssueLevelService.KeyOf(row)] = row;
    }

    public SentinelPulseIssueLevel? Find(PulseIssueKey key) => _rows.GetValueOrDefault(key);
}

/// <summary>
/// Resolves each issue type's ErrorLevel and monitor item along the chain: a monitor point's own override
/// row, then its severity default's override row, then the shipped value; <c>SourceOffline</c> and
/// <c>UrlMissing</c> go straight from their row to the shipped value. Level and item resolve independently:
/// a NULL in either column inherits, so a row that only moves the item still follows later changes to the
/// severity default. Do not report is its own flag and ends the level chain. Load, save and reset are thin
/// persistence for the Pulse Reporting tab.
/// </summary>
public sealed class PulseIssueLevelService(ISentinelDBContext db)
{
    public const byte MinLevel = 1;
    public const byte MaxLevel = 4;

    public static PulseIssueResolution Resolve(PulseIssueKey key, PulseIssueOverrides overrides)
    {
        var chain = new List<SentinelPulseIssueLevel?> { overrides.Find(key) };
        if (key.Kind == PulseIssueKind.MonitorPoint)
            chain.Add(overrides.Find(PulseIssueKey.SeverityDefault(PulseIssueKey.NormalizeSeverity(key.Severity))));

        var levelRow = chain.FirstOrDefault(r => r is not null && (r.DoNotReport || r.ErrorLevel is not null));
        var level = levelRow switch
        {
            null => PulseIssueCatalogue.ShippedLevel(key),
            { DoNotReport: true } => (byte?)null,
            _ => Math.Clamp(levelRow.ErrorLevel!.Value, MinLevel, MaxLevel),
        };

        var itemRow = chain.FirstOrDefault(r => r?.MonitorItemId is not null);
        var item = itemRow?.MonitorItemId ?? PulseIssueCatalogue.DefaultMonitorItemId;

        return new PulseIssueResolution(level, item, levelRow is not null, itemRow is not null);
    }

    public static PulseIssueKey KeyOf(SentinelPulseIssueLevel row) => row.Kind switch
    {
        PulseIssueKind.MonitorPoint => PulseIssueKey.MonitorPoint(row.MonitorPointId ?? "", PulseIssueKey.NormalizeSeverity(row.Severity)),
        PulseIssueKind.SeverityDefault => PulseIssueKey.SeverityDefault(PulseIssueKey.NormalizeSeverity(row.Severity)),
        PulseIssueKind.DirectorAlert => new PulseIssueKey(PulseIssueKind.DirectorAlert, DirectorAlertKind: row.DirectorAlertKind),
        _ => new PulseIssueKey(row.Kind),
    };

    public async Task<PulseIssueOverrides> LoadAsync(CancellationToken ct = default) =>
        new(await db.SentinelPulseIssueLevels.AsNoTracking().ToListAsync(ct));

    /// <summary>
    /// Creates or replaces the override row for one issue type. A null level or item inherits; with both
    /// inherited and reporting on there is nothing to override, so the row is deleted as by a reset.
    /// </summary>
    public async Task SaveAsync(PulseIssueKey key, byte? errorLevel, bool doNotReport, int? monitorItemId, string? user, CancellationToken ct = default)
    {
        if (errorLevel is < MinLevel or > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(errorLevel), errorLevel, "ErrorLevel overrides are 1 to 4, or null to inherit.");
        if (doNotReport && errorLevel is not null)
            throw new ArgumentException("Do not report carries no ErrorLevel.", nameof(errorLevel));

        if (errorLevel is null && !doNotReport && monitorItemId is null)
        {
            await ResetAsync(key, ct);
            return;
        }

        var row = await FindRowAsync(key, ct);
        if (row is null)
        {
            row = new SentinelPulseIssueLevel
            {
                Kind = key.Kind,
                MonitorPointId = key.MonitorPointId,
                Severity = key.Severity,
                DirectorAlertKind = key.DirectorAlertKind,
            };
            db.SentinelPulseIssueLevels.Add(row);
        }

        row.ErrorLevel = errorLevel;
        row.DoNotReport = doNotReport;
        row.MonitorItemId = monitorItemId;
        row.LastEditUser = user;
        row.LastEditDate = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Returns an issue type to its shipped values by deleting its override row.</summary>
    public async Task ResetAsync(PulseIssueKey key, CancellationToken ct = default)
    {
        var row = await FindRowAsync(key, ct);
        if (row is null) return;
        db.SentinelPulseIssueLevels.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private Task<SentinelPulseIssueLevel?> FindRowAsync(PulseIssueKey key, CancellationToken ct) =>
        db.SentinelPulseIssueLevels.FirstOrDefaultAsync(r =>
            r.Kind == key.Kind
            && r.MonitorPointId == key.MonitorPointId
            && r.Severity == key.Severity
            && r.DirectorAlertKind == key.DirectorAlertKind, ct);
}
