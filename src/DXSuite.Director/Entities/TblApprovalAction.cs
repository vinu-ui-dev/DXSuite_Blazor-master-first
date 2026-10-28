using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Audit record for each approve/decline action taken within a chain.
/// </summary>
[Table("tblApprovalAction")]
public partial class TblApprovalAction
{
    [Key]
    public int ApprovalActionId { get; set; }

    public int ApprovalRequestId { get; set; }

    public int ApprovalStageId { get; set; }

    public int UserGroupId { get; set; }

    public int ActingUserId { get; set; }

    /// <summary>
    /// "approved" or "declined"
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string ActionType { get; set; } = null!;

    [StringLength(500)]
    [Unicode(false)]
    public string? Notes { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ActionDate { get; set; }

    [ForeignKey("ApprovalRequestId")]
    [InverseProperty("Actions")]
    public virtual TblApprovalRequest ApprovalRequest { get; set; } = null!;

    [ForeignKey("ApprovalStageId")]
    public virtual TblApprovalStage ApprovalStage { get; set; } = null!;

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;

    [ForeignKey("ActingUserId")]
    public virtual TblUser ActingUser { get; set; } = null!;
}
