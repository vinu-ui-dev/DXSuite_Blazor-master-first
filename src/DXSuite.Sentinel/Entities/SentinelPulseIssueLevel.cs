using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// A tenant's override of the ErrorLevel or Pulse monitor item for one issue type. Shipped values live in
/// code (PulseIssueCatalogue), so this table holds overrides only and "Reset to default" deletes the row.
/// Tenant-scoped by living in the tenant's Sentinel DB, so it carries no customer column.
/// </summary>
[Table("SentinelPulseIssueLevel")]
public partial class SentinelPulseIssueLevel
{
    [Key]
    [Column("SentinelPulseIssueLevelID")]
    public int SentinelPulseIssueLevelId { get; set; }

    /// <summary>SeverityDefault | MonitorPoint | SourceOffline | DirectorAlert.</summary>
    [Column("Kind")]
    [StringLength(20)]
    [Unicode(false)]
    public string Kind { get; set; } = null!;

    /// <summary>Set for MonitorPoint.</summary>
    [Column("MonitorPointId")]
    [StringLength(100)]
    [Unicode(false)]
    public string? MonitorPointId { get; set; }

    /// <summary>warning | critical; set for SeverityDefault and MonitorPoint.</summary>
    [Column("Severity")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Severity { get; set; }

    /// <summary>Set for DirectorAlert (UrlMissing).</summary>
    [Column("DirectorAlertKind")]
    [StringLength(30)]
    [Unicode(false)]
    public string? DirectorAlertKind { get; set; }

    /// <summary>1 to 4; NULL means inherit along the resolution chain.</summary>
    [Column("ErrorLevel")]
    public byte? ErrorLevel { get; set; }

    /// <summary>Do not report: the issue type writes nothing to Pulse. Wins over <see cref="ErrorLevel"/>.</summary>
    [Column("DoNotReport")]
    public bool DoNotReport { get; set; }

    /// <summary>NULL means inherit along the resolution chain.</summary>
    [Column("MonitorItemID")]
    public int? MonitorItemId { get; set; }

    [Column("LastEditUser")]
    [StringLength(100)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [Column("LastEditDate", TypeName = "datetime2")]
    public DateTime LastEditDate { get; set; }
}
