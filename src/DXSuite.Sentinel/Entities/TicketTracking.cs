using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelTicketTracking")]
public partial class TicketTracking
{
    [Key]
    [Column("TrackingID")]
    public long TrackingId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("ExternalTicketID")]
    [StringLength(100)]
    [Unicode(false)]
    public string ExternalTicketId { get; set; } = null!;

    [Column("ExternalTicketUrl")]
    [StringLength(500)]
    [Unicode(false)]
    public string? ExternalTicketUrl { get; set; }

    [Column("ItsmPlatform")]
    [StringLength(50)]
    [Unicode(false)]
    public string ItsmPlatform { get; set; } = null!;

    [Column("Status")]
    [StringLength(50)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column("Title")]
    [StringLength(200)]
    [Unicode(false)]
    public string Title { get; set; } = null!;

    [Column("DeviceInstanceIds")]
    [Unicode(false)]
    public string? DeviceInstanceIds { get; set; }

    [Column("OutageCorrelationID")]
    public long? OutageCorrelationId { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastUpdated", TypeName = "datetime")]
    public DateTime? LastUpdated { get; set; }

    [Column("ResolvedAt", TypeName = "datetime")]
    public DateTime? ResolvedAt { get; set; }

    [ForeignKey("OutageCorrelationId")]
    [InverseProperty("TicketTrackings")]
    public virtual OutageCorrelation? OutageCorrelation { get; set; }
}
