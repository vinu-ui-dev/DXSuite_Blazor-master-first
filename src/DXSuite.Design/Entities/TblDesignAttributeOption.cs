using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignAttributeOption")]
public class TblDesignAttributeOption
{
    [Key]
    public int OptionId { get; set; }

    public int AttributeId { get; set; }

    [Required, MaxLength(500)]
    public string OptionValue { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    // Navigation
    public virtual TblDesignAttribute? Attribute { get; set; }
}
