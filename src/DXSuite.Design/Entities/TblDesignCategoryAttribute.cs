using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignCategoryAttribute")]
public class TblDesignCategoryAttribute
{
    [Key]
    public int AssignmentId { get; set; }

    public int CategoryId { get; set; }
    public int AttributeId { get; set; }
    public int SortOrder { get; set; }

    // Navigation
    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignCategory? Category { get; set; }

    [ForeignKey(nameof(AttributeId))]
    public virtual TblDesignAttribute? Attribute { get; set; }
}
