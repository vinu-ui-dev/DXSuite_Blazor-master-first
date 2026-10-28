using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignCategoryLaborAssignment")]
public class TblDesignCategoryLaborAssignment
{
    [Key]
    public int AssignmentId { get; set; }

    public int CategoryId { get; set; }
    public int LaborTypeId { get; set; }

    /// <summary>Default hours per unit of this item type for labor estimation.</summary>
    public decimal DefaultHours { get; set; }

    // Navigation
    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignCategory? Category { get; set; }

    [ForeignKey(nameof(LaborTypeId))]
    public virtual TblDesignLaborType? LaborType { get; set; }
}
