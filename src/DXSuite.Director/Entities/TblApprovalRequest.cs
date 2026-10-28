using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Tracks one item moving through a chain: an asset approval on a location's chain, or a canned Message on its
/// Message Type's chain (MSG-11), never both (check constraint). A Message has at most one request, reset on each
/// submission; whether it carries a new Message or a pending revision is read from the Message itself.
/// </summary>
[Table("tblApprovalRequest")]
[Index(nameof(AssetApprovalId))]
public partial class TblApprovalRequest
{
    [Key]
    public int ApprovalRequestId { get; set; }

    public int? AssetApprovalId { get; set; }

    public int? MessageId { get; set; }

    public int ApprovalChainId { get; set; }

    /// <summary>
    /// The stage currently awaiting action. Null when completed/declined.
    /// </summary>
    public int? CurrentStageId { get; set; }

    /// <summary>
    /// "pending", "approved", "declined", "withdrawn"
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = "pending";

    [Column(TypeName = "datetime")]
    public DateTime SubmittedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CompletedDate { get; set; }

    [ForeignKey("AssetApprovalId")]
    [InverseProperty("ApprovalRequests")]
    public virtual TblAssetApproval? AssetApproval { get; set; }

    [ForeignKey("MessageId")]
    public virtual TblMessage? Message { get; set; }

    [ForeignKey("ApprovalChainId")]
    public virtual TblApprovalChain ApprovalChain { get; set; } = null!;

    [ForeignKey("CurrentStageId")]
    public virtual TblApprovalStage? CurrentStage { get; set; }

    [InverseProperty("ApprovalRequest")]
    public virtual ICollection<TblApprovalQueueEntry> QueueEntries { get; set; } = new List<TblApprovalQueueEntry>();

    [InverseProperty("ApprovalRequest")]
    public virtual ICollection<TblApprovalAction> Actions { get; set; } = new List<TblApprovalAction>();
}
