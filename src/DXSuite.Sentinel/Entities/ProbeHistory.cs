using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelProbeHistory")]
public class ProbeHistory
{
    [Key]
    [Column("ProbeHistoryID")]
    public long ProbeHistoryId { get; set; }

    [Column("DeviceInstanceID")]
    public int? DeviceInstanceId { get; set; }

    /// <summary>
    /// For Director devices that don't have a Sentinel DeviceInstance record
    /// </summary>
    [Column("DirectorDeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? DirectorDeviceId { get; set; }

    [Column("ProbeTimestamp", TypeName = "datetime")]
    public DateTime ProbeTimestamp { get; set; }

    /// <summary>
    /// "online" | "offline" | "degraded"
    /// </summary>
    [Column("OverallResult")]
    [StringLength(20)]
    [Unicode(false)]
    public string OverallResult { get; set; } = null!;

    /// <summary>
    /// "green" | "yellow" | "red"
    /// </summary>
    [Column("StatusColor")]
    [StringLength(10)]
    [Unicode(false)]
    public string StatusColor { get; set; } = null!;

    [Column("ProfileName")]
    [StringLength(100)]
    [Unicode(false)]
    public string? ProfileName { get; set; }

    /// <summary>
    /// Full ProbeResult JSON with step-level details
    /// </summary>
    [Column("ResultJson")]
    [Unicode(false)]
    public string ResultJson { get; set; } = null!;

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
