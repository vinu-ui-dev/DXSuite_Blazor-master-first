using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// A Task: an operator-authored script (PowerShell/cmd/bash/JS) that agents run on demand,
/// on a schedule, or reactively. Adapted from the Graphite_Prototype_v2 TblScript model, minus
/// the content/data-object coupling (Sentinel Tasks are a service tool, not content).
/// </summary>
[Table("SentinelTask")]
public partial class SentinelTask
{
    [Key]
    [Column("TaskID")]
    public int TaskId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("Name")]
    [StringLength(100)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [Column("Description")]
    [StringLength(500)]
    [Unicode(false)]
    public string? Description { get; set; }

    /// <summary>Interpreter: powershell | cmd | bash | js.</summary>
    [Column("ScriptType")]
    [StringLength(20)]
    [Unicode(false)]
    public string ScriptType { get; set; } = null!;

    /// <summary>Where it runs: windows-agent | linux-agent | soc-player.</summary>
    [Column("ExecutionLocation")]
    [StringLength(20)]
    [Unicode(false)]
    public string ExecutionLocation { get; set; } = null!;

    [Column("ScriptContent")]
    [Unicode(false)]
    public string ScriptContent { get; set; } = null!;

    /// <summary>Lowercase hex SHA-256 of ScriptContent; sent with the command so the agent verifies integrity.</summary>
    [Column("ContentHash")]
    [StringLength(64)]
    [Unicode(false)]
    public string ContentHash { get; set; } = null!;

    /// <summary>Default execution timeout in milliseconds (overridable per run).</summary>
    [Column("TimeoutMs")]
    public int? TimeoutMs { get; set; }

    /// <summary>True when the task requires explicit operator approval before each run (high-risk).</summary>
    [Column("RequiresApproval")]
    public bool RequiresApproval { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [Column("LastEditUser")]
    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [Column("CreatedBy")]
    [StringLength(50)]
    [Unicode(false)]
    public string? CreatedBy { get; set; }

    [InverseProperty("Task")]
    public virtual ICollection<SentinelTaskTarget> Targets { get; set; } = new List<SentinelTaskTarget>();

    [InverseProperty("Task")]
    public virtual ICollection<SentinelTaskSchedule> Schedules { get; set; } = new List<SentinelTaskSchedule>();

    [InverseProperty("Task")]
    public virtual ICollection<SentinelTaskExecutionLog> ExecutionLogs { get; set; } = new List<SentinelTaskExecutionLog>();
}
