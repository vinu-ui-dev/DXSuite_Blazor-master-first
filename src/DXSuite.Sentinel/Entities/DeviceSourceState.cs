using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// One monitoring angle's view of a device. A physical device (one <see cref="DeviceInstance"/>) can be
/// watched from several sources at once — cloud connectors (Ink, BrightAuthor…), the Engage/Director
/// heartbeat, a site-gateway probe, or a self-monitoring agent. Each source upserts its own row here with
/// its own health + last-seen; the canonical DeviceInstance.LastStateJson is a computed rollup across these.
/// OverallHealth is stored as the int value of Services.OverallHealthStatus to keep Entities free of a
/// Services dependency.
/// </summary>
[Table("SentinelDeviceSourceState")]
public partial class DeviceSourceState
{
    [Key]
    [Column("DeviceSourceStateID")]
    public int DeviceSourceStateId { get; set; }

    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    /// <summary>cloud-connector | heartbeat | gateway | agent-host.</summary>
    [Column("SourceType")]
    [StringLength(20)]
    [Unicode(false)]
    public string SourceType { get; set; } = null!;

    /// <summary>Source-natural key: "connectorId:platformDeviceId" | directorDeviceId | gatewayId.</summary>
    [Column("SourceKey")]
    [StringLength(100)]
    [Unicode(false)]
    public string SourceKey { get; set; } = null!;

    /// <summary>This source's health as an int (Services.OverallHealthStatus).</summary>
    [Column("OverallHealth")]
    public int OverallHealth { get; set; }

    [Column("LastStateJson")]
    [Unicode(false)]
    public string? LastStateJson { get; set; }

    [Column("LastSeenUtc", TypeName = "datetime")]
    public DateTime LastSeenUtc { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [ForeignKey("DeviceInstanceId")]
    [InverseProperty("DeviceSourceStates")]
    public virtual DeviceInstance DeviceInstance { get; set; } = null!;
}
