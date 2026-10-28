using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// Time-based schedule for a Task (the "run at 2am nightly" case). Ported from the
/// Graphite_Prototype_v2 TblScriptSchedule model.
/// </summary>
[Table("SentinelTaskSchedule")]
public partial class SentinelTaskSchedule
{
    [Key]
    [Column("ScheduleID")]
    public int ScheduleId { get; set; }

    [Column("TaskID")]
    public int TaskId { get; set; }

    /// <summary>interval | daily | weekly | once.</summary>
    [Column("ExecutionMode")]
    [StringLength(20)]
    [Unicode(false)]
    public string ExecutionMode { get; set; } = null!;

    /// <summary>For ExecutionMode=interval: seconds between runs.</summary>
    [Column("FrequencySeconds")]
    public int? FrequencySeconds { get; set; }

    /// <summary>For weekly: comma-separated day numbers (0=Sun..6=Sat), e.g. "1,2,3,4,5".</summary>
    [Column("DaysOfWeek")]
    [StringLength(20)]
    [Unicode(false)]
    public string? DaysOfWeek { get; set; }

    /// <summary>For daily/weekly/once: UTC time of day "HH:mm:ss" (the scheduler runs in UTC).</summary>
    [Column("TimeOfDay")]
    [StringLength(8)]
    [Unicode(false)]
    public string? TimeOfDay { get; set; }

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; }

    [Column("LastRunUtc", TypeName = "datetime")]
    public DateTime? LastRunUtc { get; set; }

    [Column("NextRunUtc", TypeName = "datetime")]
    public DateTime? NextRunUtc { get; set; }

    [Column("LastStatus")]
    [StringLength(20)]
    [Unicode(false)]
    public string? LastStatus { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("TaskId")]
    [InverseProperty("Schedules")]
    public virtual SentinelTask Task { get; set; } = null!;
}
