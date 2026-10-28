using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

public enum RequirementPriority
{
    MustHave,
    ShouldHave,
    NiceToHave,
}

public enum RequirementStatus
{
    PendingReview,
    Accepted,
    NeedsClarification,
    Challenged,
}

[Table("tblDesignRequirement")]
public class TblDesignRequirement
{
    [Key]
    public int RequirementId { get; set; }

    public int DesignId { get; set; }

    public int CategoryId { get; set; }

    /// <summary>Position within the category; drives the "N.M" display numbering. Gapped values (10, 20, 30…) allow cheap reorders.</summary>
    public int SortOrder { get; set; }

    /// <summary>Stored as string via HasConversion.</summary>
    public RequirementPriority Priority { get; set; } = RequirementPriority.MustHave;

    /// <summary>Stored as string via HasConversion.</summary>
    public RequirementStatus Status { get; set; } = RequirementStatus.PendingReview;

    [Required, MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? ClientNotes { get; set; }

    /// <summary>User ID as string — no FK to avoid cross-DB constraint.</summary>
    [MaxLength(100)]
    public string? AssignedToUserId { get; set; }

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;
    public DateTime LastEditDate { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(DesignId))]
    public virtual TblDesign? Design { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignRequirementCategory? Category { get; set; }

    public virtual ICollection<TblDesignRequirementComment> Comments { get; set; } = [];
}
