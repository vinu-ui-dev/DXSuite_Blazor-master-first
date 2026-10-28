using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DXSuite.Director.Services.Reports;

/// <summary>
/// The Message Playback Log (FSD 1.1 AUD-13): every Activation, each shown version it put on screen with Sent and
/// Delivered to signage, and its frozen screens with the Display Zone each came through. It reports what was sent,
/// never what played. Showing it is not recorded (AUD-07); its exports and prints are (AUD-15).
/// </summary>
public interface IMessagePlaybackLogReport
{
    /// <summary>
    /// What the Message Type and Display Zone filters offer: deleted types and retired zones too, as past Activations
    /// still carry them.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The role lacks <c>page:message-playback-log</c>.</exception>
    Task<MessagePlaybackLogFilterOptions> FilterOptionsAsync(CancellationToken ct = default);

    /// <exception cref="UnauthorizedAccessException">The role lacks <c>page:message-playback-log</c>.</exception>
    Task<MessagePlaybackLogResult> QueryAsync(MessagePlaybackLogQuery query, CancellationToken ct = default);

    /// <summary>
    /// The Activation's screens as frozen at send (ZONE-06), by Display Zone then name. Names come from the frozen rows,
    /// so a display deleted since still reads as it was sent.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The role lacks <c>page:message-playback-log</c>.</exception>
    Task<IReadOnlyList<PlaybackScreen>> ScreensAsync(int activationId, CancellationToken ct = default);

    /// <summary>Builds the Excel export of a query and records it as <c>report.exported</c> (AUD-15).</summary>
    /// <exception cref="UnauthorizedAccessException">The role lacks <c>page:message-playback-log</c> or <c>feature:reports-export</c>.</exception>
    Task<ReportFile> ExportAsync(MessagePlaybackLogQuery query, CancellationToken ct = default);

    /// <summary>Runs a query for the print view and records it as <c>report.printed</c> (AUD-15).</summary>
    /// <exception cref="UnauthorizedAccessException">The role lacks <c>page:message-playback-log</c> or <c>feature:reports-export</c>.</exception>
    Task<MessagePlaybackLogPrint> PrintAsync(MessagePlaybackLogQuery query, CancellationToken ct = default);
}

/// <summary>What the print view shows: the Activations, and the header AUD-15 asks for.</summary>
public sealed record MessagePlaybackLogPrint(MessagePlaybackLogResult Result, ReportHeader Header);

/// <summary>
/// The report's filters. Start and End (inclusive) bound when an Activation was sent. <see cref="Find"/> or
/// <see cref="ActivationId"/> name one record and ignore the range and every other filter (AUD-18).
/// </summary>
public sealed record MessagePlaybackLogQuery
{
    public required DateTime StartUtc { get; init; }

    public required DateTime EndUtc { get; init; }

    /// <summary>The Message Type of the Activation's Message.</summary>
    public int? MessageTypeId { get; init; }

    public MessageActivationSource? Source { get; init; }

    /// <summary>A Display Zone picked at send or that any of the Activation's screens came through, retired ones included.</summary>
    public int? ZoneId { get; init; }

    /// <summary>Part of the sending account's name or sign-in name.</summary>
    public string? SentBy { get; init; }

    /// <summary>A number typed into Find: a <c>VersionId</c> from the DataPipe sheet or an Activation id.</summary>
    public int? Find { get; init; }

    /// <summary>One Activation by id, as a link from the User Event Log names it.</summary>
    public int? ActivationId { get; init; }

    public bool IgnoresRange => Find is not null || ActivationId is not null;
}

public sealed record MessagePlaybackLogFilterOptions(IReadOnlyList<MessagePlaybackLogOption> MessageTypes, IReadOnlyList<MessagePlaybackLogOption> DisplayZones);

/// <param name="Gone">A deleted Message Type or a retired Display Zone.</param>
public sealed record MessagePlaybackLogOption(int Id, string Name, bool Gone);

/// <param name="TotalCount">Every match; <paramref name="Rows"/> holds at most the newest the report shows.</param>
public sealed record MessagePlaybackLogResult(IReadOnlyList<PlaybackActivation> Rows, int TotalCount)
{
    public bool Truncated => TotalCount > Rows.Count;
}

/// <summary>One Activation as the report lists it.</summary>
/// <param name="SentBy">The sender's name, or "API" or "CUPPS" for those sources; null when the account is gone.</param>
/// <param name="SentUtc">When the Activation was sent (<c>ActivatedUtc</c>); the range filters on it.</param>
/// <param name="Zones">
/// The Display Zones its screens came through and the ones picked at send, in list order; a picked zone whose displays
/// all came through another has no screens. A null zone is a one-display send.
/// </param>
/// <param name="StartLocal">A scheduled send's wall-clock range and weekly window (<see cref="ActivationWhen"/>).</param>
public sealed record PlaybackActivation(
    int ActivationId,
    int MessageId,
    string MessageName,
    bool IsOneOff,
    string MessageTypeName,
    MessageActivationSource Source,
    string? SentBy,
    DateTime SentUtc,
    DateTime StartUtc,
    DateTime? EndUtc,
    DateTime? CancelledUtc,
    string? CancelledBy,
    ActivationStatus Status,
    int ScreenCount,
    IReadOnlyList<PlaybackZone> Zones,
    IReadOnlyList<PlaybackVersion> Versions,
    DateTime? StartLocal = null,
    DateTime? EndLocal = null,
    string? Window = null)
{
    public ActivationWhen When => new(StartUtc, EndUtc, StartLocal, EndLocal, Window);
}

/// <param name="ZoneId">Null for the screens of a one-display send.</param>
public sealed record PlaybackZone(int? ZoneId, string? Name, bool Retired, int ScreenCount);

/// <summary>A shown version (AUD-06). <see cref="FirstServedUtc"/> is null while it has never left Director.</summary>
public sealed record PlaybackVersion(
    int VersionId,
    int Sequence,
    ShownVersionReason Reason,
    int? RevisionNumber,
    string MessageTypeCode,
    int EffectiveRank,
    IReadOnlyList<PlaybackText> Texts,
    IReadOnlyList<PlaybackAsset> Assets,
    DateTime SentUtc,
    DateTime? FirstServedUtc)
{
    public string ReasonText => Reason == ShownVersionReason.Revision ? $"Revision {RevisionNumber}" : "First send";
}

/// <summary>One frozen screen row, labelled "sent" (D-09). A null zone is a one-display send.</summary>
public sealed record PlaybackScreen(int ActivationId, int? DisplayId, string DisplayName, string LocationName, int? ZoneId, string? ZoneName, bool ZoneRetired);

public sealed record PlaybackText(string LanguageCode, string Text);

public sealed record PlaybackAsset(string SlotKey, int? AssetId, string? FileName, string? ContentHash);

internal sealed class MessagePlaybackLogReport(
    IDbContextFactory<DirectorDbContext> dbFactory,
    ISessionState session,
    TimeProvider clock,
    DisplayTimeZones displayTimeZones,
    IUserEventLog eventLog,
    IServiceProvider services) : IMessagePlaybackLogReport
{
    /// <summary>The newest Activations a screen or print view shows; the count still covers every match.</summary>
    public const int ScreenRows = 1000;

    /// <summary>The newest Activations an export holds.</summary>
    public const int ExportRows = 10_000;

    /// <summary>The screen rows an export holds, inside Excel's sheet limit; one send can reach hundreds of screens.</summary>
    public const int ExportScreens = 1_000_000;

    /// <summary>The <c>report</c> value of this report's read events (AUD-08).</summary>
    public const string ReportKey = "message-playback-log";

    private const string ReportName = "Message Playback Log";

    public async Task<MessagePlaybackLogFilterOptions> FilterOptionsAsync(CancellationToken ct = default)
    {
        await RequireAsync(PermissionKeys.PageMessagePlaybackLog);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var types = await db.TblMessageTypes.AsNoTracking()
            .OrderBy(t => t.IsDeleted).ThenBy(t => t.DisplayName)
            .Select(t => new MessagePlaybackLogOption(t.MessageTypeId, t.DisplayName, t.IsDeleted))
            .ToListAsync(ct);
        var zones = await db.TblZones.AsNoTracking()
            .OrderBy(z => z.RetiredUtc != null).ThenBy(z => z.SortOrder).ThenBy(z => z.ZoneId)
            .Select(z => new MessagePlaybackLogOption(z.ZoneId, z.ZoneName, z.RetiredUtc != null))
            .ToListAsync(ct);
        return new MessagePlaybackLogFilterOptions(types, zones);
    }

    public async Task<MessagePlaybackLogResult> QueryAsync(MessagePlaybackLogQuery query, CancellationToken ct = default)
    {
        await RequireAsync(PermissionKeys.PageMessagePlaybackLog);
        return await SearchAsync(query, ScreenRows, ct);
    }

    public async Task<IReadOnlyList<PlaybackScreen>> ScreensAsync(int activationId, CancellationToken ct = default)
    {
        await RequireAsync(PermissionKeys.PageMessagePlaybackLog);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ScreensQuery(db.TblMessageActivationTargets.Where(t => t.MessageActivationId == activationId)).ToListAsync(ct);
    }

    public async Task<ReportFile> ExportAsync(MessagePlaybackLogQuery query, CancellationToken ct = default)
    {
        await RequireAsync(PermissionKeys.PageMessagePlaybackLog);
        await RequireAsync(PermissionKeys.ReportsExport);
        var result = await SearchAsync(query, ExportRows, ct);
        List<PlaybackScreen> screens;
        int screenTotal;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var ids = Newest(db, query, ExportRows).Select(a => a.MessageActivationId);
            var targets = db.TblMessageActivationTargets.Where(t => ids.Contains(t.MessageActivationId));
            screenTotal = await targets.CountAsync(ct);
            screens = await ScreensQuery(targets).Take(ExportScreens).ToListAsync(ct);
        }
        var (fields, header) = await DescribeAsync(query, ct);
        var bytes = MessagePlaybackLogExcelExporter.Build(header, result, screens,
            screenTotal > screens.Count ? $"The first {screens.Count} of {screenTotal} screen rows." : null);
        await WriteReadAsync(UserEventActionKeys.ReportExported, query, fields, result.Rows.Count, ct);
        return new ReportFile(bytes, $"MessagePlaybackLog_{header.MadeAtUtc:yyyy-MM-dd_HHmm}Z.xlsx", result.Rows.Count);
    }

    public async Task<MessagePlaybackLogPrint> PrintAsync(MessagePlaybackLogQuery query, CancellationToken ct = default)
    {
        await RequireAsync(PermissionKeys.PageMessagePlaybackLog);
        await RequireAsync(PermissionKeys.ReportsExport);
        var result = await SearchAsync(query, ScreenRows, ct);
        var (fields, header) = await DescribeAsync(query, ct);
        await WriteReadAsync(UserEventActionKeys.ReportPrinted, query, fields, result.Rows.Count, ct);
        return new MessagePlaybackLogPrint(result, header);
    }

    // rowCount is the Activations handed out, the report's records; a find names one record, so it has no range.
    private Task WriteReadAsync(string key, MessagePlaybackLogQuery query, IReadOnlyDictionary<string, string> fields, int rowCount,
        CancellationToken ct) =>
        ReportReads.WriteAsync(eventLog, key, ReportKey, ReportName, fields,
            query.IgnoresRange ? null : new { startUtc = UserEventLogFilterText.Iso(query.StartUtc), endUtc = UserEventLogFilterText.Iso(query.EndUtc) },
            rowCount, preset: null, ct);

    /// <summary>The filters that are set, by name, with Message Types and Display Zones named as they are now.</summary>
    private async Task<(IReadOnlyDictionary<string, string> Fields, ReportHeader Header)> DescribeAsync(MessagePlaybackLogQuery q, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>();
        var described = new List<string>();
        void Add(string name, string value, string? shown = null)
        {
            fields[name] = value;
            described.Add($"{name}: {shown ?? value}");
        }

        if (q.ActivationId is { } activationId) Add("activation", activationId.ToString());
        else if (q.Find is { } find) Add("find", find.ToString());
        else
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (q.MessageTypeId is { } typeId)
                Add("messageType", await db.TblMessageTypes.Where(t => t.MessageTypeId == typeId).Select(t => t.DisplayName).FirstOrDefaultAsync(ct)
                    ?? $"Message Type {typeId}");
            if (q.Source is { } source) Add("source", source.ToString());
            if (q.ZoneId is { } zoneId)
            {
                var zone = await db.TblZones.Where(z => z.ZoneId == zoneId).Select(z => new { z.ZoneName, z.RetiredUtc }).FirstOrDefaultAsync(ct);
                var name = zone?.ZoneName ?? $"Display Zone {zoneId}";
                Add("displayZone", name, zone?.RetiredUtc is null ? name : $"{name} (retired)");
            }
            if (!string.IsNullOrWhiteSpace(q.SentBy)) Add("sentBy", q.SentBy.Trim());
        }

        var header = await ReportReads.HeaderAsync(eventLog, services, q.StartUtc, q.EndUtc,
            described.Count == 0 ? "none" : string.Join("; ", described), q.IgnoresRange, clock.GetUtcNow().UtcDateTime, ct);
        return (fields, header);
    }

    // A Display Zone is deleted only while nothing targets it, so the zone a frozen row names is still there.
    private static IQueryable<PlaybackScreen> ScreensQuery(IQueryable<TblMessageActivationTarget> targets) =>
        targets.AsNoTracking()
            .OrderBy(t => t.MessageActivationId)
            .ThenBy(t => t.Zone == null ? int.MaxValue : t.Zone.SortOrder).ThenBy(t => t.ZoneId)
            .ThenBy(t => t.DisplayName).ThenBy(t => t.DisplayId)
            .Select(t => new PlaybackScreen(t.MessageActivationId, t.DisplayId, t.DisplayName, t.LocationName, t.ZoneId,
                t.Zone == null ? null : t.Zone.ZoneName, t.Zone != null && t.Zone.RetiredUtc != null));

    // Showing a report is not recorded (AUD-07), so a refusal logs nothing.
    private Task RequireAsync(string key) => session.DemandAsync(key);

    private async Task<MessagePlaybackLogResult> SearchAsync(MessagePlaybackLogQuery query, int maxRows, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var total = await Filter(db, query).CountAsync(ct);
        var activations = await Newest(db, query, maxRows)
            .Include(a => a.Message).ThenInclude(m => m.MessageType)
            .ToListAsync(ct);
        // A subquery rather than a list of ids, which an export of thousands would turn into too many parameters.
        var ids = Newest(db, query, maxRows).Select(a => a.MessageActivationId);

        var versions = (await db.TblMessageActivationVersions.AsNoTracking()
                .Where(v => ids.Contains(v.MessageActivationId))
                .ToListAsync(ct))
            .ToLookup(v => v.MessageActivationId);

        // Screen counts per Display Zone; the screens themselves are read on demand, as one send can reach hundreds.
        var zoneCounts = (await db.TblMessageActivationTargets.AsNoTracking()
                .Where(t => ids.Contains(t.MessageActivationId))
                .GroupBy(t => new { t.MessageActivationId, t.ZoneId })
                .Select(g => new { g.Key.MessageActivationId, g.Key.ZoneId, Count = g.Count() })
                .ToListAsync(ct))
            .ToLookup(z => z.MessageActivationId);
        // A Display Zone picked at send whose displays all came through another zone has no screen rows; it is listed
        // with none, as the sender chose it (AUD-13).
        var chosen = (await db.TblMessageActivationZones.AsNoTracking()
                .Where(c => ids.Contains(c.MessageActivationId))
                .Select(c => new { c.MessageActivationId, c.ZoneId })
                .ToListAsync(ct))
            .ToLookup(c => c.MessageActivationId, c => c.ZoneId);
        var zoneIds = zoneCounts.SelectMany(g => g).Select(z => z.ZoneId).OfType<int>()
            .Concat(chosen.SelectMany(g => g))
            .Distinct().ToList();
        var zones = await db.TblZones.AsNoTracking().Where(z => zoneIds.Contains(z.ZoneId)).ToDictionaryAsync(z => z.ZoneId, ct);

        var userIds = activations.SelectMany(a => new[] { a.ActivatedByUserId, a.CancelledByUserId }).OfType<int>().Distinct().ToList();
        var names = await db.TblUsers.AsNoTracking().Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FriendlyName ?? u.UserPrincipalName ?? $"User {u.UserId}", ct);

        // Only an Activation that is active or scheduled reads its screens' clocks, and few are at once.
        var activeOrScheduled = activations.Where(a => ActivationState.IsActiveOrScheduled(a, now)).Select(a => a.MessageActivationId).ToList();
        var screenDisplays = (await db.TblMessageActivationTargets.AsNoTracking()
                .Where(t => activeOrScheduled.Contains(t.MessageActivationId) && t.DisplayId != null)
                .Select(t => new { t.MessageActivationId, DisplayId = t.DisplayId!.Value })
                .Distinct()
                .ToListAsync(ct))
            .ToLookup(t => t.MessageActivationId, t => t.DisplayId);
        var statuses = await displayTimeZones.StatusesAsync(db.TblDevices, activations, screenDisplays, now, ct);

        var rows = activations.Select(a =>
        {
            var counted = zoneCounts[a.MessageActivationId].Select(z => (z.ZoneId, z.Count)).ToList();
            var picked = chosen[a.MessageActivationId].Where(id => counted.All(c => c.ZoneId != id)).Select(id => (ZoneId: (int?)id, Count: 0));
            var screens = counted.Concat(picked)
                .Select(z => z.ZoneId is { } id && zones.TryGetValue(id, out var zone)
                    ? new { Order = (zone.SortOrder, zone.ZoneId), Zone = new PlaybackZone(id, zone.ZoneName, zone.RetiredUtc is not null, z.Count) }
                    : new { Order = (int.MaxValue, int.MaxValue), Zone = new PlaybackZone(z.ZoneId, null, false, z.Count) })
                .OrderBy(z => z.Order)
                .Select(z => z.Zone)
                .ToList();
            return new PlaybackActivation(
                a.MessageActivationId, a.MessageId, a.Message.Name, !a.Message.IsCanned, a.Message.MessageType.DisplayName,
                a.Source, SentBy(a, names), a.ActivatedUtc, a.StartUtc, a.EndUtc, a.CancelledUtc,
                a.CancelledByUserId is { } by ? names.GetValueOrDefault(by) : null,
                statuses[a.MessageActivationId], screens.Sum(z => z.ScreenCount), screens,
                versions[a.MessageActivationId].OrderBy(v => v.Sequence).Select(Version).ToList(),
                a.StartLocal, a.EndLocal, ActivationTiming.DescribeWindow(a));
        }).ToList();
        return new MessagePlaybackLogResult(rows, total);
    }

    /// <summary>The newest <paramref name="maxRows"/> matches: the Activations a result holds.</summary>
    private static IQueryable<TblMessageActivation> Newest(DirectorDbContext db, MessagePlaybackLogQuery query, int maxRows) =>
        Filter(db, query).OrderByDescending(a => a.ActivatedUtc).ThenByDescending(a => a.MessageActivationId).Take(maxRows);

    private static IQueryable<TblMessageActivation> Filter(DirectorDbContext db, MessagePlaybackLogQuery q)
    {
        var activations = db.TblMessageActivations.AsNoTracking();
        if (q.ActivationId is { } activationId)
            return activations.Where(a => a.MessageActivationId == activationId);
        if (q.Find is { } id)
            return activations.Where(a => a.MessageActivationId == id || a.Versions.Any(v => v.MessageActivationVersionId == id));

        activations = activations.Where(a => a.ActivatedUtc >= q.StartUtc && a.ActivatedUtc <= q.EndUtc);
        if (q.MessageTypeId is { } typeId)
            activations = activations.Where(a => a.Message.MessageTypeId == typeId);
        if (q.Source is { } source)
            activations = activations.Where(a => a.Source == source);
        if (q.ZoneId is { } zoneId)
            activations = activations.Where(a => a.Targets.Any(t => t.ZoneId == zoneId) || a.ChosenZones.Any(c => c.ZoneId == zoneId));
        if (!string.IsNullOrWhiteSpace(q.SentBy))
        {
            var name = q.SentBy.Trim();
            var senders = db.TblUsers.Where(u => u.FriendlyName!.Contains(name) || u.UserPrincipalName!.Contains(name)).Select(u => u.UserId);
            activations = activations.Where(a => a.ActivatedByUserId != null && senders.Contains(a.ActivatedByUserId.Value));
        }
        return activations;
    }

    private static string? SentBy(TblMessageActivation activation, IReadOnlyDictionary<int, string> names) => activation.Source switch
    {
        MessageActivationSource.Api => "API",
        MessageActivationSource.Cupps => "CUPPS",
        _ => activation.ActivatedByUserId is { } id ? names.GetValueOrDefault(id) : null,
    };

    private static PlaybackVersion Version(TblMessageActivationVersion v) => new(
        v.MessageActivationVersionId, v.Sequence, v.Reason, v.RevisionNumber, v.MessageTypeCode, v.EffectiveRank,
        Texts(v.Texts), Assets(v.AssetSlots), v.SentUtc, v.FirstServedUtc);

    // The two JSON columns are written by Director alone, but a row the report cannot read is still shown, as raw text.
    private static IReadOnlyList<PlaybackText> Texts(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [new PlaybackText("", json)];
            return doc.RootElement.EnumerateObject()
                .Select(p => new PlaybackText(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText()))
                .ToList();
        }
        catch (JsonException)
        {
            return [new PlaybackText("", json)];
        }
    }

    private static IReadOnlyList<PlaybackAsset> Assets(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [new PlaybackAsset("", null, json, null)];
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => new PlaybackAsset(
                    Str(e, "slotKey") ?? "",
                    e.TryGetProperty("assetId", out var id) && id.TryGetInt32(out var n) ? n : null,
                    Str(e, "fileName"),
                    Str(e, "contentHash")))
                .ToList();
        }
        catch (JsonException)
        {
            return [new PlaybackAsset("", null, json, null)];
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }
}

public static class MessagePlaybackLogReportRegistration
{
    public static IServiceCollection AddMessagePlaybackLogReport(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMessagePlaybackLogReport, MessagePlaybackLogReport>();
        return services;
    }
}
