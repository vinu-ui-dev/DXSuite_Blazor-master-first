using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblDisplay")]
public partial class TblDisplay
{
    [Key]
    public int DisplayId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int LocationId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string DisplayName { get; set; } = null!;

    public int DisplayMode { get; set; }

    public int DisplayTypeId { get; set; }

    public int DisplayGroupId { get; set; }

    public double DisplayHeight { get; set; }

    public double DisplayWidth { get; set; }

    public int ConstraintId { get; set; }

    public int? OverrideAssetId { get; set; }

    public bool UseClock { get; set; }

    public double? ClockHeight { get; set; }

    public double? ClockWidth { get; set; }

    public bool UseVideoMask { get; set; }

    public bool EnableFade { get; set; } = true;

    /// <summary>
    /// When true, enables hotel user permission filtering and content approval workflow for schedule displays
    /// </summary>
    public bool EnableContentApproval { get; set; } = false;

    /// <summary>
    /// StatusSelector display: linked Asset Library location providing selectable content
    /// </summary>
    public int? AssetLibraryLocationId { get; set; }

    /// <summary>
    /// StatusSelector display: metadata key for grouped button mode (null = flat asset list)
    /// </summary>
    [StringLength(50)]
    [Unicode(false)]
    public string? StatusSelectorMetadataKey { get; set; }

    public int? EndpointId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? EndpointPath { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// JSON column storing display-specific settings (DisplayExtendedData), including encoding profiles.
    /// </summary>
    public string? ExtendedData { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("ConstraintId")]
    [InverseProperty("TblDisplays")]
    public virtual TblConstraint Constraint { get; set; } = null!;

    [ForeignKey("DisplayGroupId")]
    [InverseProperty("TblDisplays")]
    public virtual TblDisplayGroup DisplayGroup { get; set; } = null!;

    [ForeignKey("DisplayTypeId")]
    [InverseProperty("TblDisplays")]
    public virtual TblDisplayType DisplayType { get; set; } = null!;

    [ForeignKey("EndpointId")]
    [InverseProperty("TblDisplays")]
    public virtual TblEndpoint? Endpoint { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("TblDisplays")]
    public virtual TblLocation Location { get; set; } = null!;

    [ForeignKey("OverrideAssetId")]
    [InverseProperty("TblDisplays")]
    public virtual TblAsset? OverrideAsset { get; set; }

    [InverseProperty("Display")]
    public virtual ICollection<TblDevice> TblDevices { get; set; } = new List<TblDevice>();

    [InverseProperty("Display")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();

    [InverseProperty("Display")]
    public virtual ICollection<TblScheduleEvent> TblScheduleEvents { get; set; } = new List<TblScheduleEvent>();

    [InverseProperty("Display")]
    public virtual ICollection<TblOverrideDisplay> OverrideDisplays { get; set; } = new List<TblOverrideDisplay>();
}
