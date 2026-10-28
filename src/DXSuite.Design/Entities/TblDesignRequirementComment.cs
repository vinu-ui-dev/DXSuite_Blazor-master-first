using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignRequirementComment")]
public class TblDesignRequirementComment
{
    [Key]
    public int CommentId { get; set; }

    public int RequirementId { get; set; }

    /// <summary>Author identity stored as strings — no FK to avoid cross-DB constraint.</summary>
    [MaxLength(100)]
    public string AuthorUserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string AuthorName { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(RequirementId))]
    public virtual TblDesignRequirement? Requirement { get; set; }
}
