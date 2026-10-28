using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One logical player URL observed via its `id=` query parameter — one row per
/// monitor id, no matter how many endpoints the page fetches (the document plus
/// its API polls all beat the same row). Rows appear automatically when a tagged
/// URL starts polling (see UrlHeartbeatService); operators then enable monitoring
/// so the absence of polls raises alerts.
/// </summary>
[Table("tblDirectorUrl")]
[Index("MonitorId", IsUnique = true, Name = "UX_tblDirectorUrl_MonitorId")]
[Index("DeviceInstanceId", Name = "IX_tblDirectorUrl_DeviceInstanceId")]
public class TblDirectorUrl
{
    [Key]
    public int DirectorUrlId { get; set; }

    /// <summary>Free-form operator-assigned identity from the URL's id= parameter.</summary>
    [StringLength(64)]
    [Unicode(false)]
    public string MonitorId { get; set; } = "";

    /// <summary>
    /// Display URL (normalized: id/debug/refresh stripped, parameters sorted).
    /// Page-document requests overwrite it so it reads as the operator-entered
    /// player URL rather than an internal poll endpoint.
    /// </summary>
    [StringLength(500)]
    public string UrlKey { get; set; } = "";

    /// <summary>Parsed from the URL's locationId parameter when present.</summary>
    public int? LocationId { get; set; }

    /// <summary>
    /// Loose reference to tblDevice.DeviceId so alerts can name hardware. Not a
    /// hard FK: device rows are re-created by connector syncs and the monitor row
    /// must survive that.
    /// </summary>
    [StringLength(100)]
    public string? LinkedDeviceId { get; set; }

    /// <summary>
    /// The Sentinel DeviceInstance that reports this URL's absence to Pulse. No FK: the
    /// row lives in the tenant's Sentinel database, which Director cannot reference.
    /// </summary>
    public int? DeviceInstanceId { get; set; }

    public bool IsMonitored { get; set; }

    /// <summary>Absence threshold; the shells poll every 30s, so 120s = ~4 missed polls.</summary>
    public int ExpectedIntervalSeconds { get; set; } = 120;

    [Column(TypeName = "datetime2")]
    public DateTime FirstSeenUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime LastSeenUtc { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string? LastSeenIp { get; set; }

    /// <summary>
    /// Sweeper state machine: "online" | "missing". Persisted so an app restart
    /// neither re-alerts on an already-reported outage nor misses a recovery.
    /// </summary>
    [StringLength(16)]
    [Unicode(false)]
    public string AlertState { get; set; } = "online";

    [Column(TypeName = "datetime2")]
    public DateTime? LastTransitionUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime LastEditDate { get; set; }
}
