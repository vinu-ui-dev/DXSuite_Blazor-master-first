using System.Globalization;
using ClosedXML.Excel;

namespace DXSuite.Director.Services.Reports;

/// <summary>
/// Builds the Message Playback Log Excel export: a sheet with one row per shown version (an Activation with none yet
/// has one row with the version columns blank), and a sheet with every frozen screen row. A scheduled send's
/// StartScreenTime and EndScreenTime are wall-clock times each screen reads on its own clock, so they carry no zone.
/// </summary>
public static class MessagePlaybackLogExcelExporter
{
    private static readonly string[] Leading =
    [
        "ActivationId", "VersionId", "Sequence", "Reason", "SentUtc", "DeliveredToSignageUtc", "Message", "OneOff",
        "MessageType", "MessageTypeCode", "EffectiveRank", "Source", "SentBy", "ActivationSentUtc", "StartUtc", "EndUtc",
        "StartScreenTime", "EndScreenTime", "WeeklyWindow", "Status", "CancelledUtc", "CancelledBy", "Screens", "DisplayZones",
    ];

    public static readonly string[] ScreenColumns = ["ActivationId", "DisplayId", "Display", "Location", "DisplayZone", "ZoneRetired"];

    /// <param name="screensNote">Says the Screens sheet stops short, when it does.</param>
    public static byte[] Build(ReportHeader header, MessagePlaybackLogResult result, IReadOnlyList<PlaybackScreen> screens, string? screensNote)
    {
        // One text column per language any version carries, in the order they first appear.
        var languages = result.Rows.SelectMany(a => a.Versions).SelectMany(v => v.Texts).Select(t => t.LanguageCode).Distinct().ToList();
        string[] columns = [.. Leading, .. languages.Select(l => l.Length == 0 ? "Text" : $"Text_{l}"), "Assets"];

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Shown versions");
        var headerRow = ReportSheet.WriteHeader(sheet, "Message Playback Log", header, result.Truncated
            ? $"The newest {result.Rows.Count} of {result.TotalCount} matching Activations. Narrow the range for the rest."
            : null);
        ReportSheet.WriteColumns(sheet, headerRow, columns);
        var r = headerRow + 1;
        foreach (var a in result.Rows)
        {
            object?[] activation =
            [
                a.MessageName, a.IsOneOff ? "Yes" : "No", a.MessageTypeName,
            ];
            object?[] sending =
            [
                a.Source.ToString(), a.SentBy, UserEventLogFilterText.Iso(a.SentUtc), UserEventLogFilterText.Iso(a.StartUtc),
                a.EndUtc is { } end ? UserEventLogFilterText.Iso(end) : null,
                a.StartLocal?.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                a.EndLocal?.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), a.Window, a.Status.ToString(),
                a.CancelledUtc is { } cancelled ? UserEventLogFilterText.Iso(cancelled) : null, a.CancelledBy, a.ScreenCount,
                string.Join("; ", a.Zones.Where(z => z.Name is not null).Select(z => z.Retired ? $"{z.Name} (retired)" : z.Name)),
            ];
            if (a.Versions.Count == 0)
            {
                ReportSheet.WriteRow(sheet, r++, [a.ActivationId, null, null, null, null, null, .. activation, null, null, .. sending]);
                continue;
            }
            foreach (var v in a.Versions)
            {
                var texts = languages.Select(l => (object?)v.Texts.FirstOrDefault(t => t.LanguageCode == l)?.Text);
                ReportSheet.WriteRow(sheet, r++,
                [
                    a.ActivationId, v.VersionId, v.Sequence, v.ReasonText, UserEventLogFilterText.Iso(v.SentUtc),
                    v.FirstServedUtc is { } served ? UserEventLogFilterText.Iso(served) : null,
                    .. activation, v.MessageTypeCode, v.EffectiveRank, .. sending, .. texts,
                    string.Join("; ", v.Assets.Select(s => $"{s.SlotKey}: {s.FileName} (asset {s.AssetId}, {s.ContentHash})")),
                ]);
            }
        }
        ReportSheet.Finish(sheet, headerRow, r - 1, columns.Length);

        var screenSheet = workbook.Worksheets.Add("Screens");
        var screenHeader = ReportSheet.WriteHeader(screenSheet, "Message Playback Log: screens, fixed when sent", header, screensNote);
        ReportSheet.WriteColumns(screenSheet, screenHeader, ScreenColumns);
        r = screenHeader + 1;
        foreach (var s in screens)
        {
            ReportSheet.WriteRow(screenSheet, r++,
            [
                s.ActivationId, s.DisplayId, s.DisplayName, s.LocationName, s.ZoneName,
                s.ZoneId is null ? null : s.ZoneRetired ? "Yes" : "No",
            ]);
        }
        ReportSheet.Finish(screenSheet, screenHeader, r - 1, ScreenColumns.Length);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
