using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignWorksheetLineItemAttr")]
public class TblDesignWorksheetLineItemAttr
{
    [Key]
    public int LineItemAttrId { get; set; }

    public int LineItemId { get; set; }
    public int AttributeId { get; set; }

    [MaxLength(2000)]
    public string? Value { get; set; }

    // Navigation
    [ForeignKey(nameof(LineItemId))]
    public virtual TblDesignWorksheetLineItem? LineItem { get; set; }

    [ForeignKey(nameof(AttributeId))]
    public virtual TblDesignAttribute? Attribute { get; set; }
}
