using ClosedXML.Excel;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace DXSuite.Director.Services.Reports;

/// <summary>A generated report's header: when it was made, by whom, and the filters that shaped it (AUD-14/15).</summary>
public sealed record ReportHeader(DateTime MadeAtUtc, string MadeBy, string FilterSummary,
    DateTime? StartUtc, DateTime? EndUtc, bool IgnoresRange);

/// <summary>An exported report file, ready to hand back as a download.</summary>
public sealed record ReportFile(byte[] Bytes, string FileName, int RowCount);

/// <summary>
/// A report viewer's local time zone, detected from the browser (AUD-12: times stored UTC, shown in the viewer's
/// local time with offset). <see cref="Utc"/> is the safe default before detection runs or when it fails.
/// </summary>
public sealed class ReportTimeZone
{
    private readonly TimeZoneInfo _zone;
    private ReportTimeZone(TimeZoneInfo zone) => _zone = zone;

    public static ReportTimeZone Utc { get; } = new(TimeZoneInfo.Utc);

    /// <summary>The zone's display name (its IANA/Windows id).</summary>
    public string Name => _zone.Id;

    public static async Task<ReportTimeZone> DetectAsync(IJSRuntime js)
    {
        try
        {
            var id = await js.InvokeAsync<string>("eval", "Intl.DateTimeFormat().resolvedOptions().timeZone");
            return new ReportTimeZone(TimeZoneInfo.FindSystemTimeZoneById(id));
        }
        catch (Exception)
        {
            return Utc;
        }
    }

    public DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    /// <summary>Alias of <see cref="Local"/>.</summary>
    public DateTime ToLocal(DateTime utc) => Local(utc);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);

    /// <summary>The zone's UTC offset at <paramref name="utc"/>, formatted like "UTC-05:00".</summary>
    public string Offset(DateTime utc)
    {
        var offset = _zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
    }
}

/// <summary>
/// Shared helpers for a report's "who read it, with what filters" audit trail and its header (AUD-07, AUD-14, AUD-15:
/// a routine view is not logged — only an export or print is — so callers log explicitly, not from a shared base class).
/// </summary>
public static class ReportReads
{
    /// <summary>Writes a report-read event (an export or print — never a routine view, see AUD-07).</summary>
    public static Task WriteAsync(IUserEventLog eventLog, string actionKey, string reportKey, string reportName,
        IReadOnlyDictionary<string, string> fields, object? range, int rowCount, object? preset, CancellationToken ct) =>
        eventLog.WriteAsync(new UserEvent(actionKey, new UserEventTarget("Report", reportKey, reportName),
            Details: new { fields, range, rowCount, preset }), ct);

    /// <summary>Builds a <see cref="ReportHeader"/> for the current session, with the filters already described by the caller.</summary>
    public static async Task<ReportHeader> HeaderAsync(IUserEventLog eventLog, IServiceProvider services,
        DateTime startUtc, DateTime endUtc, string filterSummary, bool ignoresRange, DateTime madeAtUtc, CancellationToken ct)
    {
        var session = services.GetRequiredService<ISessionState>();
        var madeBy = session.User?.FriendlyName ?? session.User?.UserPrincipalName ?? "Unknown";
        await Task.CompletedTask; // reserved for a future async lookup (e.g. platform display name)
        return new ReportHeader(madeAtUtc, madeBy, filterSummary, ignoresRange ? null : startUtc, ignoresRange ? null : endUtc, ignoresRange);
    }
}

/// <summary>Writes a report's title block and column header row to an Excel worksheet, in a fixed, reusable layout.</summary>
public static class ReportSheet
{
    /// <returns>The row number the data (after the column header row) starts on.</returns>
    public static int WriteHeader(IXLWorksheet sheet, string title, ReportHeader header, string? note)
    {
        var row = 1;
        sheet.Cell(row, 1).Value = title; row++;
        sheet.Cell(row, 1).Value = $"Generated {header.MadeAtUtc:yyyy-MM-dd HH:mm} UTC by {header.MadeBy}"; row++;
        sheet.Cell(row, 1).Value = $"Filters: {header.FilterSummary}"; row++;
        if (note is not null)
        {
            sheet.Cell(row, 1).Value = note;
            row++;
        }
        row++; // blank row separating the title block from the column header row
        return row;
    }

    public static void WriteColumns(IXLWorksheet sheet, int row, IReadOnlyList<string> columns)
    {
        for (var i = 0; i < columns.Count; i++)
            sheet.Cell(row, i + 1).Value = columns[i];
    }

    public static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<object?> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            switch (values[i])
            {
                case null:
                    break;
                case DateTime dt:
                    cell.Value = dt;
                    break;
                case bool b:
                    cell.Value = b;
                    break;
                case int or long or double or decimal:
                    cell.Value = Convert.ToDouble(values[i]);
                    break;
                default:
                    cell.Value = values[i]!.ToString();
                    break;
            }
        }
    }

    /// <summary>Freezes the header rows and column-header row, and auto-fits columns once every row is written.</summary>
    public static void Finish(IXLWorksheet sheet, int columnHeaderRow, int lastDataRow, int columnCount)
    {
        sheet.SheetView.FreezeRows(columnHeaderRow);
        if (columnCount > 0) sheet.Columns(1, columnCount).AdjustToContents();
    }
}
