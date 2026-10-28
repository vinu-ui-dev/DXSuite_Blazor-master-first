using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Per-location access rule granting a user or group full, partial, or view access.
/// Lives in Director because it bridges identity entities (User/UserGroup) with Director entities (Location/ApprovalChain/AccessSchedule).
/// </summary>
[Table("tblLocationAccessRule")]
[Index(nameof(LocationId), nameof(UserId), nameof(UserGroupId), nameof(AccessLevel), IsUnique = true, Name = "IX_AccessRule_Unique_WithLevel")]
public partial class TblLocationAccessRule
{
    [Key]
    public int LocationAccessRuleId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int LocationId { get; set; }

    public int? UserId { get; set; }

    public int? UserGroupId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string AccessLevel { get; set; } = "full";

    /// <summary>
    /// What a <c>view</c> rule lets its holder see: <c>none</c> or <c>all</c>. Null on <c>full</c> and
    /// <c>partial</c> rules, where it has no meaning.
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string? ViewScope { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    // Identity navigation properties
    [ForeignKey("UserId")]
    public virtual TblUser? User { get; set; }

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup? UserGroup { get; set; }

    // Director navigation properties
    [ForeignKey("LocationId")]
    public virtual TblLocation Location { get; set; } = null!;

    [InverseProperty("LocationAccessRule")]
    public virtual ICollection<TblApprovalChain> ApprovalChains { get; set; } = new List<TblApprovalChain>();

    [InverseProperty("LocationAccessRule")]
    public virtual ICollection<TblAccessSchedule> AccessSchedules { get; set; } = new List<TblAccessSchedule>();
}
