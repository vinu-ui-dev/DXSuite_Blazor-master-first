using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// A stage within an approval chain. Stages execute sequentially (THEN).
/// Groups within a stage are connected by IntraStageOperator (AND = all must approve, OR = any can approve).
/// </summary>
[Table("tblApprovalStage")]
[Index(nameof(ApprovalChainId), nameof(StageOrder), IsUnique = true)]
public partial class TblApprovalStage
{
    [Key]
    public int ApprovalStageId { get; set; }

    public int ApprovalChainId { get; set; }

    public int StageOrder { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? StageName { get; set; }

    /// <summary>
    /// How groups within this stage relate: "AND" (all must approve) or "OR" (any can approve)
    /// </summary>
    [StringLength(3)]
    [Unicode(false)]
    public string IntraStageOperator { get; set; } = "OR";

    [ForeignKey("ApprovalChainId")]
    [InverseProperty("Stages")]
    public virtual TblApprovalChain ApprovalChain { get; set; } = null!;

    [InverseProperty("ApprovalStage")]
    public virtual ICollection<TblApprovalStageGroup> StageGroups { get; set; } = new List<TblApprovalStageGroup>();
}
