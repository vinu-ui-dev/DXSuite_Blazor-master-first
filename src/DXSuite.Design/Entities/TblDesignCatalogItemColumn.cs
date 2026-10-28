using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Item-level labor hours override for a specific column.
/// When present, replaces (does not supplement) the category default for that column on this item.
/// </summary>
[Table("tblDesignCatalogItemColumn")]
public class TblDesignCatalogItemColumn
{
    [Key]
    public int OverrideId { get; set; }

    public int ItemId { get; set; }
    public int ColumnId { get; set; }

    public decimal Hours { get; set; }

    // Navigation
    [ForeignKey(nameof(ItemId))]
    public virtual TblDesignCatalogItem? Item { get; set; }

    [ForeignKey(nameof(ColumnId))]
    public virtual TblDesignLaborColumn? Column { get; set; }
}
