using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignCatalogItemAttribute")]
public class TblDesignCatalogItemAttribute
{
    [Key]
    public int ValueId { get; set; }

    public int ItemId { get; set; }
    public int AttributeId { get; set; }

    /// <summary>Stored as string regardless of AttributeType. Validated/rendered client-side.</summary>
    [MaxLength(2000)]
    public string? Value { get; set; }

    [ForeignKey(nameof(ItemId))]
    public virtual TblDesignCatalogItem? Item { get; set; }

    [ForeignKey(nameof(AttributeId))]
    public virtual TblDesignAttribute? Attribute { get; set; }
}
