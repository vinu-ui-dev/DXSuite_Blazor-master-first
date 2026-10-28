using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// An approval chain, owned by exactly one location or one Message Type (check constraint, ZONE-09). A location can
/// have multiple chains: one default chain and optionally one per access rule. A Message Type has at most one.
/// Stages are connected by implicit THEN (sequential); within each stage, groups
/// are connected by the stage's IntraStageOperator (AND or OR).
/// </summary>
[Table("tblApprovalChain")]
[Index(nameof(LocationId))]
public partial class TblApprovalChain
{
    [Key]
    public int ApprovalChainId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int? LocationId { get; set; }

    public int? MessageTypeId { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// True for the location's default chain; false for rule-specific chains
    /// </summary>
    public bool IsDefault { get; set; } = true;

    /// <summary>
    /// FK to the access rule this chain applies to (null for default chain)
    /// </summary>
    public int? LocationAccessRuleId { get; set; }

    /// <summary>
    /// When true, uploads by users who are members of any approver group are auto-approved
    /// </summary>
    public bool AutoApproveMembers { get; set; }

    /// <summary>
    /// When true, content that passes LLM moderation is auto-approved through the chain
    /// </summary>
    public bool EnableLlmAutoApproval { get; set; }

    /// <summary>
    /// 0-indexed stage order up to which LLM auto-approval applies.
    /// Only meaningful when EnableLlmAutoApproval is true.
    /// </summary>
    public int LlmAutoApprovalUpToStageOrder { get; set; }

    /// <summary>
    /// Days to retain declined content before auto-deletion. Default 3.
    /// </summary>
    public int DeclinedContentRetentionDays { get; set; } = 3;

    public const int ReminderIntervalHoursDefault = 24;
    public const int ReminderIntervalHoursMinimum = 1;

    /// <summary>
    /// When true, the current stage's approvers are emailed on each submission and get a reminder digest every
    /// <see cref="ReminderIntervalHours"/> until it is decided (FSD 1.1 section 12 point 7). Off on chains that predate it.
    /// </summary>
    public bool EmailApprovers { get; set; }

    /// <summary>Hours between reminder digests to an approver; at least <see cref="ReminderIntervalHoursMinimum"/>.</summary>
    public int ReminderIntervalHours { get; set; } = ReminderIntervalHoursDefault;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("ApprovalChains")]
    public virtual TblLocation? Location { get; set; }

    [ForeignKey("MessageTypeId")]
    public virtual TblMessageType? MessageType { get; set; }

    [ForeignKey("LocationAccessRuleId")]
    [InverseProperty("ApprovalChains")]
    public virtual TblLocationAccessRule? LocationAccessRule { get; set; }

    [InverseProperty("ApprovalChain")]
    public virtual ICollection<TblApprovalStage> Stages { get; set; } = new List<TblApprovalStage>();
}
