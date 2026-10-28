using ClosedXML.Excel;
using DXSuite.Design.Entities;
using DXSuite.Design.Models;

namespace DXSuite.Design.Services;

/// <summary>
/// Builds the Requirements workbook for a design. Two flavors share one layout:
/// the internal flavor adds Status and Assigned To columns; the customer flavor omits them.
/// Numbering mirrors the Requirements tab — categories with at least one requirement are
/// numbered by SortOrder, requirements sub-numbered within their category.
/// </summary>
public static class RequirementsExcelExporter
{
    private static readonly XLColor DxBlue = XLColor.FromHtml("#0071BC");
    private static readonly XLColor BandFill = XLColor.FromHtml("#E8F1F8");

    public static byte[] Build(
        TblDesign design,
        NetsuiteOpportunity? opportunity,
        IReadOnlyList<TblDesignRequirementCategory> categories,
        IReadOnlyList<TblDesignRequirement> requirements,
        IReadOnlyDictionary<string, string> userNames,
        bool includeInternalColumns)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Requirements");
        var lastCol = includeInternalColumns ? 6 : 4;

        // ── Header block ─────────────────────────────────────────────────────
        var row = 1;
        ws.Cell(row, 1).Value = "Design Requirements";
        ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(DxBlue);
        ws.Range(row, 1, row, lastCol).Merge();
        row++;

        ws.Cell(row, 1).Value = design.Name;
        ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12);
        ws.Range(row, 1, row, lastCol).Merge();
        row += 2;

        row = AddInfoRow(ws, row, "Opportunity:", opportunity?.TranId);
        row = AddInfoRow(ws, row, "Customer:", opportunity?.Entity);
        row = AddInfoRow(ws, row, "Sales Rep:", opportunity?.SalesRep);
        row = AddInfoRow(ws, row, "Date:", DateTime.Now.ToString("MMMM d, yyyy"));
        row++;

        // ── Column headers ───────────────────────────────────────────────────
        var headers = includeInternalColumns
            ? new[] { "#", "Requirement", "Priority", "Client Notes", "Status", "Assigned To" }
            : new[] { "#", "Requirement", "Priority", "Client Notes" };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(row, c + 1).Value = headers[c];
        var headerRange = ws.Range(row, 1, row, lastCol);
        headerRange.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        headerRange.Style.Fill.SetBackgroundColor(DxBlue);
        headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.SheetView.FreezeRows(row);
        row++;

        // ── Category bands and requirement rows ──────────────────────────────
        var catNumber = 0;
        foreach (var cat in categories.OrderBy(c => c.SortOrder))
        {
            var items = requirements
                .Where(r => r.CategoryId == cat.CategoryId)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.CreateDate)
                .ToList();
            if (items.Count == 0) continue;
            catNumber++;

            ws.Cell(row, 1).Value = $"{catNumber}. {cat.Name}";
            var band = ws.Range(row, 1, row, lastCol);
            band.Merge();
            band.Style.Font.SetBold();
            band.Style.Fill.SetBackgroundColor(BandFill);
            band.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            band.Style.Border.SetBottomBorderColor(DxBlue);
            row++;

            var itemNumber = 0;
            foreach (var req in items)
            {
                itemNumber++;
                ws.Cell(row, 1).Value = $"{catNumber}.{itemNumber}";
                ws.Cell(row, 1).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Cell(row, 2).Value = req.Description;
                ws.Cell(row, 2).Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Cell(row, 3).Value = PriorityLabel(req.Priority);
                ws.Cell(row, 3).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Cell(row, 4).Value = req.ClientNotes ?? string.Empty;
                ws.Cell(row, 4).Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                if (includeInternalColumns)
                {
                    ws.Cell(row, 5).Value = StatusLabel(req.Status);
                    ws.Cell(row, 5).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                    ws.Cell(row, 6).Value = req.AssignedToUserId is not null
                        ? userNames.GetValueOrDefault(req.AssignedToUserId, req.AssignedToUserId)
                        : string.Empty;
                    ws.Cell(row, 6).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                }
                ws.Range(row, 1, row, lastCol).Style.Border.SetBottomBorder(XLBorderStyleValues.Hair);
                row++;
            }
            row++; // blank spacer row between categories
        }

        // ── Layout ───────────────────────────────────────────────────────────
        ws.Column(1).Width = 8;
        ws.Column(2).Width = 60;
        ws.Column(3).Width = 14;
        ws.Column(4).Width = 40;
        if (includeInternalColumns)
        {
            ws.Column(5).Width = 18;
            ws.Column(6).Width = 20;
        }
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PagesWide = 1;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Writes a label/value pair; skipped entirely when the value is empty so the header stays tidy.</summary>
    private static int AddInfoRow(IXLWorksheet ws, int row, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return row;
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = value;
        return row + 1;
    }

    private static string PriorityLabel(RequirementPriority p) => p switch
    {
        RequirementPriority.MustHave => "Must Have",
        RequirementPriority.ShouldHave => "Should Have",
        RequirementPriority.NiceToHave => "Nice to Have",
        _ => p.ToString(),
    };

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PendingReview => "Pending Review",
        RequirementStatus.Accepted => "Accepted",
        RequirementStatus.NeedsClarification => "Needs Clarification",
        RequirementStatus.Challenged => "Challenged",
        _ => s.ToString(),
    };
}
