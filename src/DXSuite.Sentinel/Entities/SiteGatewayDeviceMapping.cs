using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelSiteGatewayDeviceMapping")]
public partial class SiteGatewayDeviceMapping
{
    [Key]
    [Column("MappingID")]
    public int MappingId { get; set; }

    [Column("GatewayID")]
    public int GatewayId { get; set; }

    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("LanIpAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string? LanIpAddress { get; set; }

    [Column("LanPort")]
    public int? LanPort { get; set; }

    [Column("IsReachable")]
    public bool IsReachable { get; set; }

    [Column("LastChecked", TypeName = "datetime")]
    public DateTime? LastChecked { get; set; }

    [ForeignKey("GatewayId")]
    [InverseProperty("SiteGatewayDeviceMappings")]
    public virtual SiteGateway SiteGateway { get; set; } = null!;

    [ForeignKey("DeviceInstanceId")]
    [InverseProperty("SiteGatewayDeviceMappings")]
    public virtual DeviceInstance DeviceInstance { get; set; } = null!;
}
