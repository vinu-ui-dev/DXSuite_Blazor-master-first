using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelProbeHistorySummary")]
public class ProbeHistorySummary
{
    [Key]
    [Column("SummaryID")]
    public long SummaryId { get; set; }

    [Column("DeviceInstanceID")]
    public int? DeviceInstanceId { get; set; }

    [Column("DirectorDeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? DirectorDeviceId { get; set; }

    /// <summary>
    /// "hourly" | "daily"
    /// </summary>
    [Column("Period")]
    [StringLength(10)]
    [Unicode(false)]
    public string Period { get; set; } = null!;

    /// <summary>
    /// Start of the aggregation window (UTC)
    /// </summary>
    [Column("PeriodStart", TypeName = "datetime")]
    public DateTime PeriodStart { get; set; }

    [Column("TotalProbes")]
    public int TotalProbes { get; set; }

    [Column("PassedCount")]
    public int PassedCount { get; set; }

    [Column("DegradedCount")]
    public int DegradedCount { get; set; }

    [Column("FailedCount")]
    public int FailedCount { get; set; }

    /// <summary>
    /// (PassedCount + DegradedCount) / TotalProbes * 100
    /// </summary>
    [Column("UptimePercent")]
    public float UptimePercent { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
