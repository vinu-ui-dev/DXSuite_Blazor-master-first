using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Default labor hours for a category × column pair. Applied to all line items in the category
/// unless overridden at the item level by TblDesignCatalogItemColumn.
/// </summary>
[Table("tblDesignCategoryColumnAssignment")]
public class TblDesignCategoryColumnAssignment
{
    [Key]
    public int AssignmentId { get; set; }

    public int CategoryId { get; set; }
    public int ColumnId { get; set; }

    public decimal DefaultHours { get; set; }

    // Navigation
    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignCategory? Category { get; set; }

    [ForeignKey(nameof(ColumnId))]
    public virtual TblDesignLaborColumn? Column { get; set; }
}
