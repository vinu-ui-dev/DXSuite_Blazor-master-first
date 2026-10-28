using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelDeviceDescriptor")]
public partial class DeviceDescriptor
{
    [Key]
    [Column("DescriptorID")]
    public int DescriptorId { get; set; }

    [Column("DeviceType")]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceType { get; set; } = null!;

    [Column("DisplayName")]
    [StringLength(100)]
    [Unicode(false)]
    public string? DisplayName { get; set; }

    [Column("Manufacturer")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Manufacturer { get; set; }

    [Column("Category")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Category { get; set; }

    [Column("CurrentVersion")]
    [StringLength(20)]
    [Unicode(false)]
    public string? CurrentVersion { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [Column("LastEditUser")]
    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [InverseProperty("DeviceDescriptor")]
    public virtual ICollection<DeviceDescriptorVersion> DeviceDescriptorVersions { get; set; } = new List<DeviceDescriptorVersion>();

    [InverseProperty("DeviceDescriptor")]
    public virtual ICollection<DeviceInstance> DeviceInstances { get; set; } = new List<DeviceInstance>();

    [InverseProperty("DeviceDescriptor")]
    public virtual ICollection<AutomationDefinition> AutomationDefinitions { get; set; } = new List<AutomationDefinition>();
}
