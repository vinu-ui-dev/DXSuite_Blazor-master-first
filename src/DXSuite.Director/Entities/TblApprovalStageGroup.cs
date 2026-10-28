using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Links a user group to an approval stage. Each group in a stage
/// produces a queue entry when the stage becomes active.
/// </summary>
[Table("tblApprovalStageGroup")]
[Index(nameof(ApprovalStageId), nameof(UserGroupId), IsUnique = true)]
public partial class TblApprovalStageGroup
{
    [Key]
    public int ApprovalStageGroupId { get; set; }

    public int ApprovalStageId { get; set; }

    public int UserGroupId { get; set; }

    public int SortOrder { get; set; }

    [ForeignKey("ApprovalStageId")]
    [InverseProperty("StageGroups")]
    public virtual TblApprovalStage ApprovalStage { get; set; } = null!;

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;
}
