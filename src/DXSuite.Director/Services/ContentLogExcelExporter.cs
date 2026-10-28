using ClosedXML.Excel;

namespace DXSuite.Director.Services;

public class ContentLogExportRow
{
    public DateTime FromLocal { get; init; }
    public DateTime? ToLocal { get; init; }
    public string Version { get; init; } = "";
    public string ItemName { get; init; } = "";
    public int PlaysPerLoop { get; init; }
    public int DurationSeconds { get; init; }
    public string EstimatedPlays { get; init; } = "";
    public string Window { get; init; } = "";
}

/// <summary>
/// Builds the Content Log Excel export: one row per item per interval, with a branded
/// header row. Kept static and byte-array based so the page can hand the result to the
/// token download endpoint.
/// </summary>
public static class ContentLogExcelExporter
{
    public static byte[] Build(string bucketName, string variant, IReadOnlyList<ContentLogExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Content Log");

        sheet.Cell(1, 1).Value = $"Content Log: {bucketName}" + (variant == "all" ? "" : $" (filter {variant})");
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Range(1, 1, 1, 8).Merge();

        var headers = new[] { "From", "To", "Item", "Plays per loop", "Duration (s)", "Estimated plays", "Schedule window", "Version" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(3, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#0071BC"));
        }

        var rowIndex = 4;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.FromLocal;
            sheet.Cell(rowIndex, 1).Style.DateFormat.Format = "mm/dd/yyyy hh:mm AM/PM";
            if (row.ToLocal.HasValue)
            {
                sheet.Cell(rowIndex, 2).Value = row.ToLocal.Value;
                sheet.Cell(rowIndex, 2).Style.DateFormat.Format = "mm/dd/yyyy hh:mm AM/PM";
            }
            else
            {
                sheet.Cell(rowIndex, 2).Value = "Now";
            }
            sheet.Cell(rowIndex, 3).Value = row.ItemName;
            sheet.Cell(rowIndex, 4).Value = row.PlaysPerLoop;
            sheet.Cell(rowIndex, 5).Value = row.DurationSeconds;
            sheet.Cell(rowIndex, 6).Value = row.EstimatedPlays;
            sheet.Cell(rowIndex, 7).Value = row.Window;
            sheet.Cell(rowIndex, 8).Value = row.Version;
            rowIndex++;
        }

        sheet.SheetView.FreezeRows(3);
        sheet.Columns().AdjustToContents(3, Math.Max(4, rowIndex - 1));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
