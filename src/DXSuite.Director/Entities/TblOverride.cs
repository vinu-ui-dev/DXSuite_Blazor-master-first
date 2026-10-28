using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblOverride")]
public partial class TblOverride
{
    [Key]
    public int OverrideId { get; set; }

    [Required]
    [StringLength(100)]
    public string OverrideName { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; }

    public bool IsScheduled { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? StartDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? EndDate { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public bool Sunday { get; set; }

    public bool Monday { get; set; }

    public bool Tuesday { get; set; }

    public bool Wednesday { get; set; }

    public bool Thursday { get; set; }

    public bool Friday { get; set; }

    public bool Saturday { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreatedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ModifiedDate { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    [StringLength(100)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Tracks the last schedule state evaluation to detect transitions.
    /// True = schedule was active, False = schedule was inactive, Null = never evaluated
    /// </summary>
    public bool? LastScheduleState { get; set; }

    /// <summary>
    /// Timestamp of when the schedule last triggered an activation
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? LastScheduleActivation { get; set; }

    /// <summary>
    /// Timestamp of when the schedule last triggered a deactivation
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? LastScheduleDeactivation { get; set; }

    // Navigation properties
    [InverseProperty("Override")]
    public virtual ICollection<TblOverrideDevice> OverrideDevices { get; set; } = new List<TblOverrideDevice>();

    [InverseProperty("Override")]
    public virtual ICollection<TblOverrideContent> OverrideContents { get; set; } = new List<TblOverrideContent>();

    [InverseProperty("Override")]
    public virtual ICollection<TblOverrideDisplay> OverrideDisplays { get; set; } = new List<TblOverrideDisplay>();
}
