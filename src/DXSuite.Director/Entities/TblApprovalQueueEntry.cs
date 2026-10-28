using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// A pending work item for a specific group within a stage.
/// Status: "pending" -> "approved"/"declined"/"superseded"
/// </summary>
[Table("tblApprovalQueueEntry")]
public partial class TblApprovalQueueEntry
{
    [Key]
    public int QueueEntryId { get; set; }

    public int ApprovalRequestId { get; set; }

    public int ApprovalStageId { get; set; }

    public int UserGroupId { get; set; }

    /// <summary>
    /// "pending", "approved", "declined", "superseded"
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = "pending";

    [Column(TypeName = "datetime")]
    public DateTime CreatedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ResolvedDate { get; set; }

    public int? ResolvedByUserId { get; set; }

    /// <summary>
    /// Optimistic concurrency token for safe concurrent approvals
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;

    [ForeignKey("ApprovalRequestId")]
    [InverseProperty("QueueEntries")]
    public virtual TblApprovalRequest ApprovalRequest { get; set; } = null!;

    [ForeignKey("ApprovalStageId")]
    public virtual TblApprovalStage ApprovalStage { get; set; } = null!;

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;

    [ForeignKey("ResolvedByUserId")]
    public virtual TblUser? ResolvedByUser { get; set; }
}
