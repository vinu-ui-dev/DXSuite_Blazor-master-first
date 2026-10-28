using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// An individual materials item within a category (e.g. "Network Cable - Plenum CAT6").
/// Each item links to its own NetSuite inventory item and pushes as an individual estimate line.
/// Qty per worksheet is stored in TblDesignWorksheetMaterialsRow.
/// </summary>
[Table("tblDesignMaterialsItem")]
public class TblDesignMaterialsItem
{
    [Key]
    public int ItemId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>NetSuite internal item ID used when pushing this item to an estimate.</summary>
    [MaxLength(100)]
    public string? NetsuiteItemId { get; set; }

    /// <summary>The NS item's display identifier / SKU (e.g. "CABLE : CAT6-P"). Shown in the materials table.</summary>
    [MaxLength(200)]
    public string? ModelNumber { get; set; }

    /// <summary>Per-unit cost used as the default when this item is added to a worksheet.</summary>
    public decimal DefaultCost { get; set; }

    /// <summary>Per-unit sell price used as the default when this item is added to a worksheet.</summary>
    public decimal DefaultSellPrice { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignMaterialsCategory? Category { get; set; }
}
