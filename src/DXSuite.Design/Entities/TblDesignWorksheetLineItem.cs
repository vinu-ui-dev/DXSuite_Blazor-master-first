using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignWorksheetLineItem")]
public class TblDesignWorksheetLineItem
{
    [Key]
    public int LineItemId { get; set; }

    public int WorksheetId { get; set; }
    public int? RoomId { get; set; }
    public int? CatalogItemId { get; set; }

    public int Qty { get; set; } = 1;

    /// <summary>Percentage discount applied to the item's cost before margin calculation.</summary>
    public decimal DiscountPct { get; set; }

    /// <summary>Gross margin percentage used to compute sell price from discounted cost.</summary>
    public decimal MarginPct { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Free-text label for the AV system this item belongs to within the project
    /// (e.g. "Boardroom AV", "Main Stage"). Replaces the structured Room FK for worksheet purposes.
    /// </summary>
    [MaxLength(200)]
    public string? SystemName { get; set; }

    /// <summary>When true the row is excluded from calculations and NetSuite push but remains visible so the designer can re-enable it.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>When true, custcol_acs_pdf_view is set to true on the NetSuite estimate line so the row appears in the PDF export.</summary>
    public bool PrintInPdf { get; set; }

    /// <summary>
    /// Per-worksheet CategoryGroup override. When set, this row is grouped under this group
    /// in the hardware list instead of the category's default group. Null means use the
    /// category's group. Cleared when the user permanently saves the group change to the category entity.
    /// </summary>
    public int? CategoryGroupIdOverride { get; set; }

    /// <summary>NS location override for this line item. Null means use the worksheet default location.</summary>
    [MaxLength(100)]
    public string? LocationId { get; set; }

    [MaxLength(300)]
    public string? LocationName { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(RoomId))]
    public virtual TblDesignWorksheetRoom? Room { get; set; }

    [ForeignKey(nameof(CatalogItemId))]
    public virtual TblDesignCatalogItem? CatalogItem { get; set; }

    public virtual ICollection<TblDesignWorksheetLineItemAttr> AttributeValues { get; set; } = [];
}
