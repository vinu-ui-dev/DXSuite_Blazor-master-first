using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Threaded comment on an approval workflow. Spans the entire lifecycle of an asset
/// approval — from initial submission through re-submissions and approvals/declines.
/// </summary>
[Table("tblApprovalComment")]
public partial class TblApprovalComment
{
    [Key]
    public int ApprovalCommentId { get; set; }

    /// <summary>
    /// Links to a specific chain request. Nullable for flat (non-chain) approvals.
    /// </summary>
    public int? ApprovalRequestId { get; set; }

    /// <summary>
    /// Always set — ties the comment to the asset-level approval record.
    /// </summary>
    public int AssetApprovalId { get; set; }

    public int UserId { get; set; }

    [StringLength(2000)]
    public string CommentText { get; set; } = null!;

    /// <summary>
    /// "submit", "resubmit", "approve", "decline", "general"
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string CommentType { get; set; } = "general";

    [Column(TypeName = "datetime")]
    public DateTime CreatedDate { get; set; }

    [ForeignKey("ApprovalRequestId")]
    public virtual TblApprovalRequest? ApprovalRequest { get; set; }

    [ForeignKey("AssetApprovalId")]
    public virtual TblAssetApproval AssetApproval { get; set; } = null!;

    [ForeignKey("UserId")]
    public virtual TblUser User { get; set; } = null!;
}
