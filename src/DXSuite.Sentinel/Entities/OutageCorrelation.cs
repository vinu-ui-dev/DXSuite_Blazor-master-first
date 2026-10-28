using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelOutageCorrelation")]
public partial class OutageCorrelation
{
    [Key]
    [Column("CorrelationID")]
    public long CorrelationId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("CorrelationType")]
    [StringLength(50)]
    [Unicode(false)]
    public string CorrelationType { get; set; } = null!;

    [Column("Scope")]
    [StringLength(200)]
    [Unicode(false)]
    public string Scope { get; set; } = null!;

    [Column("AffectedDeviceCount")]
    public int AffectedDeviceCount { get; set; }

    [Column("DeviceInstanceIds")]
    [Unicode(false)]
    public string DeviceInstanceIds { get; set; } = null!;

    [Column("DetectedAt", TypeName = "datetime")]
    public DateTime DetectedAt { get; set; }

    [Column("ResolvedAt", TypeName = "datetime")]
    public DateTime? ResolvedAt { get; set; }

    [Column("Status")]
    [StringLength(50)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [InverseProperty("OutageCorrelation")]
    public virtual ICollection<TicketTracking> TicketTrackings { get; set; } = new List<TicketTracking>();
}
