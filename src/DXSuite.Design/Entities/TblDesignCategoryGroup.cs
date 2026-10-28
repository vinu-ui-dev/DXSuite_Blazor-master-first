using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Broad grouping for catalog categories (e.g. "Audio", "Video", "Networking").
/// Categories reference this entity via CategoryGroupId.
/// </summary>
[Table("tblDesignCategoryGroup")]
public class TblDesignCategoryGroup
{
    [Key]
    public int CategoryGroupId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    // Navigation
    public virtual ICollection<TblDesignCategory> Categories { get; set; } = [];
}
