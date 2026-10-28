using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Named grouping for labor types (e.g. "Installation", "Cabling", "System Build").
/// Labor types reference this entity via LaborCategoryId.
/// </summary>
[Table("tblDesignLaborCategory")]
public class TblDesignLaborCategory
{
    [Key]
    public int LaborCategoryId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    // No navigation back to TblDesignLaborType — that FK was removed in Phase I-A
    // to decouple labor types from labor categories (columns now drive assignment).
}
