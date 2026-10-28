using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Time-based schedule for a floor plan element's icon.
/// Active schedule with lowest SortOrder wins; falls back to TblFloorPlanElement.IconPath.
/// </summary>
[Table("tblFloorPlanIconSchedule")]
public partial class TblFloorPlanIconSchedule : IScheduleWindow
{
    [Key]
    public int FloorPlanIconScheduleId { get; set; }

    public int FloorPlanElementId { get; set; }

    /// <summary>
    /// Source library for icon assets (alternative to direct IconAssetId)
    /// </summary>
    public int? AssetLibraryLocationId { get; set; }

    /// <summary>
    /// Metadata key for matching (used with AssetLibraryLocationId)
    /// </summary>
    [StringLength(50)]
    [Unicode(false)]
    public string? MetadataKey { get; set; }

    /// <summary>
    /// Metadata value to match (used with MetadataKey)
    /// </summary>
    [StringLength(255)]
    [Unicode(false)]
    public string? MetadataValue { get; set; }

    /// <summary>
    /// Direct asset reference for the icon (alternative to metadata-based matching)
    /// </summary>
    public int? IconAssetId { get; set; }

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

    /// <summary>
    /// Priority (lowest active wins)
    /// </summary>
    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("FloorPlanElementId")]
    [InverseProperty("IconSchedules")]
    public virtual TblFloorPlanElement FloorPlanElement { get; set; } = null!;

    [ForeignKey("IconAssetId")]
    public virtual TblAsset? IconAsset { get; set; }

    [ForeignKey("AssetLibraryLocationId")]
    public virtual TblLocation? AssetLibraryLocation { get; set; }
}
