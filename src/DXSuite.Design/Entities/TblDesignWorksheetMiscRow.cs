using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// A line item in the worksheet Miscellaneous section.
/// Name, Description, Cost and SellPrice are snapshotted at the time the item is added
/// so that catalog edits don't silently change existing worksheets.
/// </summary>
[Table("tblDesignWorksheetMiscRow")]
public class TblDesignWorksheetMiscRow
{
    [Key]
    public int RowId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Link back to the catalog item; null when the item was imported ad-hoc from NetSuite.</summary>
    public int? MiscItemId { get; set; }

    /// <summary>Snapshot of the linked NetSuite item ID — used when pushing the estimate.</summary>
    [MaxLength(100)]
    public string? NetsuiteItemId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int Qty { get; set; } = 1;
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public int SortOrder { get; set; }

    /// <summary>When true, custcol_acs_pdf_view is set on the NetSuite estimate line so the row appears in the PDF export.</summary>
    public bool PrintInPdf { get; set; }

    /// <summary>NS location override for this row. Null means use the worksheet default location.</summary>
    [MaxLength(100)]
    public string? LocationId { get; set; }

    [MaxLength(300)]
    public string? LocationName { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(MiscItemId))]
    public virtual TblDesignMiscItem? MiscItem { get; set; }
}
