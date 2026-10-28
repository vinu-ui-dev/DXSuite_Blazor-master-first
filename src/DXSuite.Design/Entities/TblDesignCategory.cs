using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignCategory")]
public class TblDesignCategory
{
    [Key]
    public int CategoryId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>Optional broad grouping (e.g. "Audio", "Video"). Null = ungrouped.</summary>
    public int? CategoryGroupId { get; set; }

    // Navigation
    public virtual TblDesignCategoryGroup? CategoryGroup { get; set; }
    public virtual ICollection<TblDesignCategoryLaborAssignment> LaborAssignments { get; set; } = [];
    public virtual ICollection<TblDesignCategoryAttribute> CategoryAttributes { get; set; } = [];
    public virtual ICollection<TblDesignCatalogItem> CatalogItems { get; set; } = [];
}
