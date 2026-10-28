using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Maps a display/role combination to a wall controller endpoint.
/// PlatformData carries platform-specific payload as JSON
/// (e.g., Pixera: screenHandle, zoneX/Y/W/H; Avalonia: windowId, rect).
/// </summary>
[Table("tblWallLayoutMapping")]
[Index("EndpointId", Name = "IX_tblWallLayoutMapping_Endpoint")]
[Index("DisplayId", Name = "IX_tblWallLayoutMapping_Display")]
public class TblWallLayoutMapping
{
    [Key]
    public int WallLayoutMappingId { get; set; }

    public int EndpointId { get; set; }

    public int DisplayId { get; set; }

    /// <summary>Layer role: "background" | "mask" | "status"</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string LayerRole { get; set; } = "status";

    /// <summary>Platform-specific JSON payload (Pixera, Avalonia, etc.).</summary>
    public string? PlatformData { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime LastEditDate { get; set; }

    [ForeignKey("EndpointId")]
    public virtual TblEndpoint Endpoint { get; set; } = null!;

    [ForeignKey("DisplayId")]
    public virtual TblDisplay Display { get; set; } = null!;
}
