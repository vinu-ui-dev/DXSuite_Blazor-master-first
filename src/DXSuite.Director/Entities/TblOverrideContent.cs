using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblOverrideContent")]
[Index(nameof(OverrideId), Name = "IX_tblOverrideContent_OverrideId")]
[Index(nameof(DeviceId), Name = "IX_tblOverrideContent_DeviceId")]
[Index(nameof(ResolutionWidth), nameof(ResolutionHeight), Name = "IX_tblOverrideContent_Resolution")]
public partial class TblOverrideContent
{
    [Key]
    public int OverrideContentId { get; set; }

    public int OverrideId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    public int ResolutionWidth { get; set; }

    public int ResolutionHeight { get; set; }

    [Required]
    [StringLength(500)]
    public string ContentPath { get; set; } = null!;

    public int? AssetId { get; set; }

    public int Duration { get; set; } = 7;

    [Column(TypeName = "datetime")]
    public DateTime CreatedDate { get; set; }

    // Navigation properties
    [ForeignKey("OverrideId")]
    [InverseProperty("OverrideContents")]
    public virtual TblOverride Override { get; set; } = null!;

    [ForeignKey("DeviceId")]
    [InverseProperty("OverrideContents")]
    public virtual TblDevice? Device { get; set; }

    [ForeignKey("AssetId")]
    [InverseProperty("OverrideContents")]
    public virtual TblAsset? Asset { get; set; }
}
