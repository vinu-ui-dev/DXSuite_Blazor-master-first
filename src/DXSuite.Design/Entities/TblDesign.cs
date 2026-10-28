using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesign")]
public class TblDesign
{
    [Key]
    public int DesignId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string NetsuiteOpportunityId { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the NetSuite Opportunity at time of creation or last refresh.</summary>
    public string? NetsuiteOpportunityData { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;
    public DateTime LastEditDate { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual ICollection<TblDesignWorksheet> Worksheets { get; set; } = [];
    public virtual ICollection<TblDesignRequirement> Requirements { get; set; } = [];
    public virtual ICollection<TblDesignRequirementCategory> RequirementCategories { get; set; } = [];
}
