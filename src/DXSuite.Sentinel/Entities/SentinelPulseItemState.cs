using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// What the Pulse publisher last wrote for one (device, monitor item). A new reading is written only when
/// (ErrorLevel, OpenIssueCodes) changes or an open item is due for its retention re-assert; Summary is
/// rendered text and never counts as a change.
/// </summary>
[Table("SentinelPulseItemState")]
[PrimaryKey(nameof(DeviceInstanceId), nameof(MonitorItemId))]
public partial class SentinelPulseItemState
{
    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("MonitorItemID")]
    public int MonitorItemId { get; set; }

    [Column("ErrorLevel")]
    public byte ErrorLevel { get; set; }

    /// <summary>Sorted open issue codes, including qualifiers such as UrlMissing:{MonitorId}.</summary>
    [Column("OpenIssueCodes")]
    [StringLength(1000)]
    [Unicode(false)]
    public string OpenIssueCodes { get; set; } = null!;

    [Column("Summary")]
    [StringLength(500)]
    [Unicode(false)]
    public string Summary { get; set; } = null!;

    [Column("LastPublishedUtc", TypeName = "datetime2")]
    public DateTime LastPublishedUtc { get; set; }
}
