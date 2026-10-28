using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelDeviceDescriptorVersion")]
public partial class DeviceDescriptorVersion
{
    [Key]
    [Column("VersionID")]
    public int VersionId { get; set; }

    [Column("DescriptorID")]
    public int DescriptorId { get; set; }

    [Column("Version")]
    [StringLength(20)]
    [Unicode(false)]
    public string Version { get; set; } = null!;

    [Column("DescriptorJson")]
    [Unicode(false)]
    public string DescriptorJson { get; set; } = null!;

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("PublishedAt", TypeName = "datetime")]
    public DateTime? PublishedAt { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("CreatedBy")]
    [StringLength(50)]
    [Unicode(false)]
    public string? CreatedBy { get; set; }

    [ForeignKey("DescriptorId")]
    [InverseProperty("DeviceDescriptorVersions")]
    public virtual DeviceDescriptor DeviceDescriptor { get; set; } = null!;

    [InverseProperty("DeviceDescriptorVersion")]
    public virtual ICollection<DeviceInstance> DeviceInstances { get; set; } = new List<DeviceInstance>();
}
