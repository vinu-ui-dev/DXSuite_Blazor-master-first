using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// Where a Task runs — its deployment scope. A task can target a single device/gateway, a whole
/// device descriptor (make/model), an entire customer, or all agents.
/// </summary>
[Table("SentinelTaskTarget")]
public partial class SentinelTaskTarget
{
    [Key]
    [Column("TargetID")]
    public int TargetId { get; set; }

    [Column("TaskID")]
    public int TaskId { get; set; }

    /// <summary>device | gateway | descriptor | customer | all.</summary>
    [Column("TargetScope")]
    [StringLength(20)]
    [Unicode(false)]
    public string TargetScope { get; set; } = null!;

    /// <summary>The id/value for the scope (device instance id, gateway id, descriptor type, customer code); null for "all".</summary>
    [Column("TargetRef")]
    [StringLength(100)]
    [Unicode(false)]
    public string? TargetRef { get; set; }

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("TaskId")]
    [InverseProperty("Targets")]
    public virtual SentinelTask Task { get; set; } = null!;
}
