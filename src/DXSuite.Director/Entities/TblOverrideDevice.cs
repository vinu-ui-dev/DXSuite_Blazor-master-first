using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblOverrideDevice")]
[Index(nameof(DeviceId), Name = "IX_tblOverrideDevice_DeviceId")]
[Index(nameof(OverrideId), Name = "IX_tblOverrideDevice_OverrideId")]
public partial class TblOverrideDevice
{
    [Key]
    public int OverrideDeviceId { get; set; }

    public int OverrideId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    // Navigation properties
    [ForeignKey("OverrideId")]
    [InverseProperty("OverrideDevices")]
    public virtual TblOverride Override { get; set; } = null!;

    [ForeignKey("DeviceId")]
    [InverseProperty("OverrideDevices")]
    public virtual TblDevice Device { get; set; } = null!;
}
