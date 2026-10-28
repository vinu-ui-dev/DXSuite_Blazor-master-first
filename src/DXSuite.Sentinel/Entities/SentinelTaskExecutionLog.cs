using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// One execution of a Task against one target — the audit trail (exit code, stdout/stderr,
/// who/what triggered it). Failed runs feed the Reporter → ITSM ticketing path.
/// </summary>
[Table("SentinelTaskExecutionLog")]
public partial class SentinelTaskExecutionLog
{
    [Key]
    [Column("ExecutionID")]
    public long ExecutionId { get; set; }

    [Column("TaskID")]
    public int TaskId { get; set; }

    /// <summary>The device this run targeted (null for gateway-host or fleet runs).</summary>
    [Column("DeviceInstanceID")]
    public int? DeviceInstanceId { get; set; }

    /// <summary>The gateway/agent this run executed on (logical reference).</summary>
    [Column("GatewayID")]
    public int? GatewayId { get; set; }

    [Column("Status")]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!; // running | success | failed | timeout

    [Column("ExitCode")]
    public int? ExitCode { get; set; }

    [Column("OutputLog")]
    [Unicode(false)]
    public string? OutputLog { get; set; }

    [Column("ErrorMessage")]
    [Unicode(false)]
    public string? ErrorMessage { get; set; }

    /// <summary>What initiated it: "manual:{user}" | "schedule:{scheduleId}" | "reactive:{automationId}".</summary>
    [Column("TriggeredBy")]
    [StringLength(100)]
    [Unicode(false)]
    public string? TriggeredBy { get; set; }

    [Column("DurationMs")]
    public long? DurationMs { get; set; }

    [Column("StartUtc", TypeName = "datetime")]
    public DateTime StartUtc { get; set; }

    [Column("EndUtc", TypeName = "datetime")]
    public DateTime? EndUtc { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("TaskId")]
    [InverseProperty("ExecutionLogs")]
    public virtual SentinelTask Task { get; set; } = null!;
}
