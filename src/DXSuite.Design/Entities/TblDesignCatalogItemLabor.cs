using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Item-level labor override. When present for a given LaborTypeId, replaces
/// (does not add to) the category-level default hours for that item.
/// </summary>
[Table("tblDesignCatalogItemLabor")]
public class TblDesignCatalogItemLabor
{
    [Key]
    public int OverrideId { get; set; }

    public int ItemId { get; set; }
    public int LaborTypeId { get; set; }
    public decimal Hours { get; set; }

    [ForeignKey(nameof(ItemId))]
    public virtual TblDesignCatalogItem? Item { get; set; }

    [ForeignKey(nameof(LaborTypeId))]
    public virtual TblDesignLaborType? LaborType { get; set; }
}
