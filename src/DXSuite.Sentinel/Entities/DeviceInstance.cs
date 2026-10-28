using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelDeviceInstance")]
public partial class DeviceInstance
{
    [Key]
    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("DescriptorID")]
    public int DescriptorId { get; set; }

    [Column("DescriptorVersionID")]
    public int? DescriptorVersionId { get; set; }

    [Column("DirectorDeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? DirectorDeviceId { get; set; }

    [Column("PulseDeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? PulseDeviceId { get; set; }

    [Column("PulseCustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string? PulseCustomerId { get; set; }

    [Column("DisplayName")]
    [StringLength(150)]
    [Unicode(false)]
    public string DisplayName { get; set; } = null!;

    [Column("IpAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string? IpAddress { get; set; }

    [Column("MacAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string? MacAddress { get; set; }

    [Column("Port")]
    public int? Port { get; set; }

    [Column("CredentialsJson")]
    [Unicode(false)]
    public string? CredentialsJson { get; set; }

    [Column("ConfigJson")]
    [Unicode(false)]
    public string? ConfigJson { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("LocationID")]
    public int? LocationId { get; set; }

    [Column("SiteGatewayID")]
    public int? SiteGatewayId { get; set; }

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; }

    [Column("LastStateJson")]
    [Unicode(false)]
    public string? LastStateJson { get; set; }

    [Column("LastStateUpdated", TypeName = "datetime")]
    public DateTime? LastStateUpdated { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    /// <summary>
    /// Actual or estimated device deployment date. Falls back to CreateDate when null.
    /// </summary>
    [Column("CommissionedDate", TypeName = "datetime")]
    public DateTime? CommissionedDate { get; set; }

    /// <summary>
    /// How CommissionedDate was determined: "auto" (from CreateDate), "llm", or "manual"
    /// </summary>
    [Column("CommissionedSource")]
    [StringLength(20)]
    [Unicode(false)]
    public string? CommissionedSource { get; set; }

    [ForeignKey("DescriptorId")]
    [InverseProperty("DeviceInstances")]
    public virtual DeviceDescriptor DeviceDescriptor { get; set; } = null!;

    [ForeignKey("DescriptorVersionId")]
    [InverseProperty("DeviceInstances")]
    public virtual DeviceDescriptorVersion? DeviceDescriptorVersion { get; set; }

    [ForeignKey("SiteGatewayId")]
    [InverseProperty("DeviceInstances")]
    public virtual SiteGateway? SiteGateway { get; set; }

    [InverseProperty("DeviceInstance")]
    public virtual ICollection<FloorplanDevice> FloorplanDevices { get; set; } = new List<FloorplanDevice>();

    [InverseProperty("DeviceInstance")]
    public virtual ICollection<PlatformDeviceLink> PlatformDeviceLinks { get; set; } = new List<PlatformDeviceLink>();

    [InverseProperty("DeviceInstance")]
    public virtual ICollection<DeviceSourceState> DeviceSourceStates { get; set; } = new List<DeviceSourceState>();

    [InverseProperty("DeviceInstance")]
    public virtual ICollection<RemediationLog> RemediationLogs { get; set; } = new List<RemediationLog>();

    [InverseProperty("DeviceInstance")]
    public virtual ICollection<SiteGatewayDeviceMapping> SiteGatewayDeviceMappings { get; set; } = new List<SiteGatewayDeviceMapping>();
}
