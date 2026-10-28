using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelPlatformDeviceLink")]
public partial class PlatformDeviceLink
{
    [Key]
    [Column("LinkID")]
    public int LinkId { get; set; }

    [Column("ConnectorID")]
    public int ConnectorId { get; set; }

    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("PlatformDeviceID")]
    [StringLength(100)]
    [Unicode(false)]
    public string PlatformDeviceId { get; set; } = null!;

    [Column("MacAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string? MacAddress { get; set; }

    [Column("PlatformMetadataJson")]
    [Unicode(false)]
    public string? PlatformMetadataJson { get; set; }

    [Column("LastSyncAt", TypeName = "datetime")]
    public DateTime? LastSyncAt { get; set; }

    [ForeignKey("DeviceInstanceId")]
    [InverseProperty("PlatformDeviceLinks")]
    public virtual DeviceInstance DeviceInstance { get; set; } = null!;
}
