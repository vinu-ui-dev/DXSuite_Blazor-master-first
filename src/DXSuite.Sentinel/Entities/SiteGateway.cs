using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelSiteGateway")]
public partial class SiteGateway
{
    [Key]
    [Column("GatewayID")]
    public int GatewayId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("SiteName")]
    [StringLength(100)]
    [Unicode(false)]
    public string SiteName { get; set; } = null!;

    [Column("ApiKeyHash")]
    [StringLength(128)]
    [Unicode(false)]
    public string ApiKeyHash { get; set; } = null!;

    [Column("Status")]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column("LastHeartbeat", TypeName = "datetime")]
    public DateTime? LastHeartbeat { get; set; }

    [Column("Version")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Version { get; set; }

    [Column("ConfigJson")]
    [Unicode(false)]
    public string? ConfigJson { get; set; }

    [Column("IpAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string? IpAddress { get; set; }

    /// <summary>Stable machine fingerprint of the agent that self-enrolled into this gateway (idempotency key).</summary>
    [Column("EnrollmentFingerprint")]
    [StringLength(100)]
    [Unicode(false)]
    public string? EnrollmentFingerprint { get; set; }

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [InverseProperty("SiteGateway")]
    public virtual ICollection<DeviceInstance> DeviceInstances { get; set; } = new List<DeviceInstance>();

    [InverseProperty("SiteGateway")]
    public virtual ICollection<SiteGatewayDeviceMapping> SiteGatewayDeviceMappings { get; set; } = new List<SiteGatewayDeviceMapping>();
}
