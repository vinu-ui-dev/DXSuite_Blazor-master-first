using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// A line item in the worksheet Travel section.
/// Travel costs are included in the All Us and Our Leads scenario totals but excluded from
/// the Subs scenario — the economic advantage of using sub contractors is they don't incur travel.
/// Snapshots Name, Description, Cost and SellPrice at add time to preserve worksheet integrity.
/// </summary>
[Table("tblDesignWorksheetTravelRow")]
public class TblDesignWorksheetTravelRow
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

    /// <summary>
    /// Snapshot from TblDesignMiscItem.IsPerPerson at the time the row was added.
    /// When true, cost is multiplied by the number of traveling personnel for each scenario.
    /// When false, cost is flat — charged once if any personnel travel, $0 if none do.
    /// </summary>
    public bool IsPerPerson { get; set; } = true;

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
