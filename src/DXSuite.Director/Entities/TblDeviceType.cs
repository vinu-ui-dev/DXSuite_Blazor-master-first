using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblDeviceType")]
public class TblDeviceType
{
    [Key]
    public int DeviceTypeId { get; set; }

    [Required]
    [StringLength(50)]
    public string TypeName { get; set; } = null!;

    [StringLength(255)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [InverseProperty("DeviceType")]
    public virtual ICollection<TblDevice> TblDevices { get; set; } = new List<TblDevice>();
}
