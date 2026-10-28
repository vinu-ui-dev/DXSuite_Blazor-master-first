using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Stores the non-zero quantity for one materials item on one worksheet.
/// Rows are created on first qty entry and deleted when qty is set back to zero.
/// All active catalog items are shown in the UI regardless of whether a row exists;
/// missing rows implicitly have Qty = 0.
/// </summary>
[Table("tblDesignWorksheetMaterialsRow")]
public class TblDesignWorksheetMaterialsRow
{
    [Key]
    public int RowId { get; set; }

    public int WorksheetId { get; set; }
    public int ItemId { get; set; }

    public int Qty { get; set; } = 1;

    /// <summary>Snapshot of DefaultCost at the time the row was first created.</summary>
    public decimal Cost { get; set; }

    /// <summary>Snapshot of DefaultSellPrice at the time the row was first created.</summary>
    public decimal SellPrice { get; set; }

    public int SortOrder { get; set; }

    /// <summary>NS location override for this row. Null means use the worksheet default location.</summary>
    [MaxLength(100)]
    public string? LocationId { get; set; }

    [MaxLength(300)]
    public string? LocationName { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(ItemId))]
    public virtual TblDesignMaterialsItem? Item { get; set; }
}
