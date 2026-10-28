using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Requirement category scoped to a single design. Each design starts with the
/// AVIXA-derived defaults and can add, remove, rename, and reorder them freely.
/// </summary>
[Table("tblDesignRequirementCategory")]
public class TblDesignRequirementCategory
{
    /// <summary>Default categories seeded into every new design, in display order (AVIXA needs-analysis structure).</summary>
    public static readonly string[] DefaultNames =
    [
        "Space & Environment",
        "Functional Goals",
        "User Experience",
        "Technical Infrastructure",
        "Budget & Schedule",
        "Constraints",
    ];

    [Key]
    public int CategoryId { get; set; }

    public int DesignId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(DesignId))]
    public virtual TblDesign? Design { get; set; }

    public virtual ICollection<TblDesignRequirement> Requirements { get; set; } = [];
}
