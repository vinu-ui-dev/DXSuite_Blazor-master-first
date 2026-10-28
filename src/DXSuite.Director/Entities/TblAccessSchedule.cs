using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Time-based schedule window for an access rule. Zero schedules on a rule = always active.
/// Supports date ranges, time-of-day windows (including overnight), and day-of-week filtering.
/// Optionally scoped to a specific display and/or content constraint.
/// </summary>
[Table("tblAccessSchedule")]
public partial class TblAccessSchedule : IScheduleWindow
{
    [Key]
    public int AccessScheduleId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int LocationAccessRuleId { get; set; }

    /// <summary>
    /// Null = location-wide; non-null = display-specific schedule
    /// </summary>
    public int? DisplayId { get; set; }

    /// <summary>
    /// Content constraint applied during this time window
    /// </summary>
    public int? ConstraintId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? StartDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? EndDate { get; set; }

    [Column(TypeName = "time")]
    public TimeOnly? StartTime { get; set; }

    [Column(TypeName = "time")]
    public TimeOnly? EndTime { get; set; }

    public bool Sunday { get; set; }
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    /// <summary>
    /// Optional metadata key for grouping content buttons when this schedule's constraint is active
    /// </summary>
    [StringLength(50)]
    [Unicode(false)]
    public string? MetadataKey { get; set; }

    [ForeignKey("LocationAccessRuleId")]
    [InverseProperty("AccessSchedules")]
    public virtual TblLocationAccessRule LocationAccessRule { get; set; } = null!;

    [ForeignKey("DisplayId")]
    public virtual TblDisplay? Display { get; set; }

    [ForeignKey("ConstraintId")]
    public virtual TblConstraint? Constraint { get; set; }
}
