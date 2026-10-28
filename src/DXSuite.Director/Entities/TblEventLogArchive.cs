using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblEventLogArchive")]
[Index(nameof(ArchivedDate), Name = "IX_tblEventLogArchive_ArchivedDate")]
[Index(nameof(CreateDate), Name = "IX_tblEventLogArchive_CreateDate")]
public class TblEventLogArchive
{
    [Key]
    public long ArchiveId { get; set; }

    public int OriginalRecordId { get; set; }

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

    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    public string? Details { get; set; }

    public bool? Success { get; set; }

    public bool TicketCreated { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TicketId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ArchivedDate { get; set; }
}
