using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelAutomationExecutionLog")]
public partial class AutomationExecutionLog
{
    [Key]
    [Column("ExecutionID")]
    public long ExecutionId { get; set; }

    [Column("AutomationID")]
    public int AutomationId { get; set; }

    [Column("AutomationVersionID")]
    public int AutomationVersionId { get; set; }

    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("Status")]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column("StepsExecuted")]
    public int StepsExecuted { get; set; }

    [Column("ExecutionTraceJson")]
    [Unicode(false)]
    public string? ExecutionTraceJson { get; set; }

    [Column("DurationMs")]
    public long? DurationMs { get; set; }

    [Column("ErrorMessage")]
    [StringLength(500)]
    [Unicode(false)]
    public string? ErrorMessage { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("CompletedAt", TypeName = "datetime")]
    public DateTime? CompletedAt { get; set; }

    [ForeignKey("AutomationId")]
    [InverseProperty("AutomationExecutionLogs")]
    public virtual AutomationDefinition AutomationDefinition { get; set; } = null!;

    [ForeignKey("AutomationVersionId")]
    [InverseProperty("AutomationExecutionLogs")]
    public virtual AutomationVersion AutomationVersion { get; set; } = null!;
}
