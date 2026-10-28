using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignCatalogItem")]
public class TblDesignCatalogItem
{
    [Key]
    public int ItemId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    public int? CategoryId { get; set; }

    [MaxLength(100)]
    public string NetsuiteItemId { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the NetSuite item fields at time of add or last refresh.</summary>
    public string? SnapshotJson { get; set; }

    public DateTime? LastSyncDate { get; set; }

    /// <summary>Denormalized from snapshot on save; avoids deserializing JSON on every worksheet load.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(200)]
    public string? Manufacturer { get; set; }

    /// <summary>NetSuite ItemId field, typically the model number (e.g. "QM-65T").</summary>
    [MaxLength(200)]
    public string? ModelName { get; set; }

    /// <summary>Catalog cost from NetSuite snapshot, cached for worksheet calculations.</summary>
    public decimal Cost { get; set; }

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignCategory? Category { get; set; }

    public virtual ICollection<TblDesignCatalogItemLabor> LaborOverrides { get; set; } = [];
    public virtual ICollection<TblDesignCatalogItemColumn> ColumnOverrides { get; set; } = [];
    public virtual ICollection<TblDesignCatalogItemAttribute> AttributeValues { get; set; } = [];
}
