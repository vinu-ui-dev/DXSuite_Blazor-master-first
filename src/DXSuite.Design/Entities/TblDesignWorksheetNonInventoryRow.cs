using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

public enum NonInventoryRowType
{
    Shipping,
    InstallMaterials,
    Accommodations,
    Custom,
}

[Table("tblDesignWorksheetNonInventoryRow")]
public class TblDesignWorksheetNonInventoryRow
{
    [Key]
    public int RowId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Stored as string via HasConversion.</summary>
    public NonInventoryRowType RowType { get; set; }

    [MaxLength(200)]
    public string? Label { get; set; }

    [MaxLength(100)]
    public string? NetsuiteItemId { get; set; }

    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }

    public int SortOrder { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }
}
