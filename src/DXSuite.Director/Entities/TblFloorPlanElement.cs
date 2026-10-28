using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// A positioned element on a floor plan location — either a child location or a display.
/// Exactly one of ChildLocationId or ChildDisplayId must be non-null.
/// </summary>
[Table("tblFloorPlanElement")]
public partial class TblFloorPlanElement
{
    [Key]
    public int FloorPlanElementId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The floor plan location that owns this element
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// Positioned location (mutually exclusive with ChildDisplayId)
    /// </summary>
    public int? ChildLocationId { get; set; }

    /// <summary>
    /// Positioned display (mutually exclusive with ChildLocationId)
    /// </summary>
    public int? ChildDisplayId { get; set; }

    public double PositionTop { get; set; }

    public double PositionLeft { get; set; }

    public double ElementWidth { get; set; }

    public double ElementHeight { get; set; }

    /// <summary>
    /// Custom icon path (null = default or inline StandardDisplay preview)
    /// </summary>
    [StringLength(255)]
    [Unicode(false)]
    public string? IconPath { get; set; }

    /// <summary>
    /// Text label displayed on or near the element
    /// </summary>
    [StringLength(50)]
    [Unicode(false)]
    public string? IconLabel { get; set; }

    /// <summary>
    /// When true, renders a live StandardDisplay preview instead of an icon card
    /// </summary>
    public bool ShowLivePreview { get; set; }

    // Geo coordinates for map modes (Google Maps / Offline Map)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // 3D coordinates for Three.js model mode
    public double? Position3DX { get; set; }
    public double? Position3DY { get; set; }
    public double? Position3DZ { get; set; }

    /// <summary>
    /// Z-index for layering
    /// </summary>
    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("FloorPlanElements")]
    public virtual TblLocation Location { get; set; } = null!;

    [ForeignKey("ChildLocationId")]
    public virtual TblLocation? ChildLocation { get; set; }

    [ForeignKey("ChildDisplayId")]
    public virtual TblDisplay? ChildDisplay { get; set; }

    [InverseProperty("FloorPlanElement")]
    public virtual ICollection<TblFloorPlanIconSchedule> IconSchedules { get; set; } = new List<TblFloorPlanIconSchedule>();
}
