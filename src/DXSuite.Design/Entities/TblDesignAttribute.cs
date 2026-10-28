using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

public enum AttributeType
{
    Text,
    Number,
    Dollars,
    List,
    Multiselect,
}

[Table("tblDesignAttribute")]
public class TblDesignAttribute
{
    [Key]
    public int AttributeId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Stored as string via HasConversion — see DesignDbContext.OnModelCreating.</summary>
    public AttributeType AttributeType { get; set; } = AttributeType.Text;

    /// <summary>
    /// When true, this attribute appears on every catalog item regardless of category assignment.
    /// Useful for infrastructure attributes (LAN Drops, Power Outlets, PoE) that apply universally.
    /// </summary>
    public bool IsGlobal { get; set; }

    /// <summary>
    /// System attributes are required for worksheet calculations (LAN Drops, PoE Power, Power Outlets, Shipping).
    /// They cannot be deleted or have their type changed.
    /// </summary>
    public bool IsSystem { get; set; }

    // Navigation
    public virtual ICollection<TblDesignAttributeOption> Options { get; set; } = [];
    public virtual ICollection<TblDesignCategoryAttribute> CategoryAttributes { get; set; } = [];
}
