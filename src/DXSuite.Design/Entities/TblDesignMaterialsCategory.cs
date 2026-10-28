using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// A customer-scoped grouping of materials items (e.g. "Bulk Cabling & Connectors").
/// Categories are purely organizational; each item within them pushes to NetSuite individually.
/// </summary>
[Table("tblDesignMaterialsCategory")]
public class TblDesignMaterialsCategory
{
    [Key]
    public int CategoryId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// NS item used as the push target when an individual materials item has no NS item of its own.
    /// The item's name is sent as the description override so the estimator can identify it in NS.
    /// </summary>
    [MaxLength(100)]
    public string? NetsuiteItemId { get; set; }

    /// <summary>Snapshot of the NS item's display name at the time it was linked.</summary>
    [MaxLength(300)]
    public string? NetsuiteItemName { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual ICollection<TblDesignMaterialsItem> Items { get; set; } = [];
}
