using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Per-location approval chain configuration. Each location can have multiple chains:
/// one default chain and optionally one per access rule.
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

    public int LocationId { get; set; }

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

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("ApprovalChains")]
    public virtual TblLocation Location { get; set; } = null!;

    [ForeignKey("LocationAccessRuleId")]
    [InverseProperty("ApprovalChains")]
    public virtual TblLocationAccessRule? LocationAccessRule { get; set; }

    [InverseProperty("ApprovalChain")]
    public virtual ICollection<TblApprovalStage> Stages { get; set; } = new List<TblApprovalStage>();
}
