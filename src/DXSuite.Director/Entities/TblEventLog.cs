using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblEventLog")]
public partial class TblEventLog
{
    [Key]
    public int RecordId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? EventSource { get; set; }

    public int EventTypeId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? EventTitle { get; set; }

    [Unicode(false)]
    public string? EventMessage { get; set; }

    public int EventSeverityId { get; set; }

    public int? UserId { get; set; }

    public int? AssetId { get; set; }

    public int? DisplayId { get; set; }

    public int? ScheduleEventId { get; set; }

    public int? LocationId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    // Device event columns
    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    public string? Details { get; set; }  // JSON for event-specific data

    public bool? Success { get; set; }  // For action events

    // ITSM integration
    public bool TicketCreated { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TicketId { get; set; }

    [ForeignKey("AssetId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblAsset? Asset { get; set; }

    [ForeignKey("DeviceId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblDevice? Device { get; set; }

    [ForeignKey("DisplayId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblDisplay? Display { get; set; }

    [ForeignKey("EventSeverityId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblEventSeverity EventSeverity { get; set; } = null!;

    [ForeignKey("EventTypeId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblEventType EventType { get; set; } = null!;

    [ForeignKey("LocationId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblLocation? Location { get; set; }

    [ForeignKey("ScheduleEventId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblScheduleEvent? ScheduleEvent { get; set; }
}
