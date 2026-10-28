using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Per-worksheet column hour override for a specific line item.
/// Takes precedence over the catalog-level TblDesignCatalogItemColumn value during calculation,
/// without modifying the shared catalog item — allowing per-design customization.
/// </summary>
[Table("tblDesignWorksheetLineItemColumnHours")]
public class TblDesignWorksheetLineItemColumnHours
{
    [Key]
    public int Id { get; set; }

    public int WorksheetId { get; set; }
    public int LineItemId { get; set; }
    public int ColumnId { get; set; }

    /// <summary>Per-unit hours override for this item × column combination on this worksheet.</summary>
    public decimal Hours { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(LineItemId))]
    public virtual TblDesignWorksheetLineItem? LineItem { get; set; }

    [ForeignKey(nameof(ColumnId))]
    public virtual TblDesignLaborColumn? Column { get; set; }
}
