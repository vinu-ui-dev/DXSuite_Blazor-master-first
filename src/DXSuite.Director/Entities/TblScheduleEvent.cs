using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblScheduleEvent")]
public partial class TblScheduleEvent : IScheduleWindow
{
    [Key]
    public int ScheduleEventId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int? ScheduleEventTypeId { get; set; }

    public int DisplayId { get; set; }

    public int? LocationId { get; set; }

    public string? AirlineCode { get; set; }

    [Unicode(false)]
    public string ContentAssignment { get; set; } = null!;

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? StartDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? EndDate { get; set; }

    public bool DefaultContent { get; set; }

    public bool Sunday { get; set; }

    public bool Monday { get; set; }

    public bool Tuesday { get; set; }

    public bool Wednesday { get; set; }

    public bool Thursday { get; set; }

    public bool Friday { get; set; }

    public bool Saturday { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    /// <summary>
    /// Airline user group responsible for this schedule event.
    /// Preferred going forward; AirlineCode retained for back-compat with legacy callers.
    /// </summary>
    public int? UserGroupId { get; set; }

    /// <summary>
    /// Per-event library override. When null, cluster resolution falls back to the
    /// group default (TblUserGroupLocationDefault) or the location's GlobalConstraintId.
    /// </summary>
    public int? OverrideConstraintId { get; set; }

    /// <summary>
    /// Asset pinned as the counter content when this schedule block becomes active.
    /// Null = no pinned default; the counter-content endpoint resolves from the constraint library.
    /// </summary>
    public int? DefaultAssetId { get; set; }

    [ForeignKey("DisplayId")]
    [InverseProperty("TblScheduleEvents")]
    public virtual TblDisplay Display { get; set; } = null!;

    [ForeignKey("UserGroupId")]
    public virtual DXSuite.Shared.Entities.Identity.TblUserGroup? UserGroup { get; set; }

    [ForeignKey("OverrideConstraintId")]
    public virtual TblConstraint? OverrideConstraint { get; set; }

    [ForeignKey("DefaultAssetId")]
    public virtual TblAsset? DefaultAsset { get; set; }

    [InverseProperty("ScheduleEvent")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();
}
