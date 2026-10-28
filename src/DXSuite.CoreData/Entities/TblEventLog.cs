using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

[Table("tblEventLog")]
[Index("RecordId", Name = "IX_tblEventLog", IsUnique = true)]
public partial class TblEventLog
{
    [Key]
    [Column("RecordID")]
    public int RecordId { get; set; }

    [Column("FlightRecordID")]
    public int? FlightRecordId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? EventSource { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? EventCategory { get; set; }

    [Column("EventTypeID")]
    public int? EventTypeId { get; set; }

    [Column("DeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    [Column("UserID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? UserId { get; set; }

    [StringLength(8)]
    [Unicode(false)]
    public string? CurrentCarousel { get; set; }

    [StringLength(8)]
    [Unicode(false)]
    public string? NewCarousel { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? EventTitle { get; set; }

    [Unicode(false)]
    public string? EventMessage { get; set; }

    [Column("IATA_Code")]
    [StringLength(3)]
    [Unicode(false)]
    public string? IataCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? CorrelationId { get; set; }

    [StringLength(1)]
    [Unicode(false)]
    public string? FlightType { get; set; }

    [Column("FLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ScheduledTime { get; set; }

    [Unicode(false)]
    public string? UpdateResult { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? PartnerFlights { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [ForeignKey("EventTypeId")]
    [InverseProperty("TblEventLogs")]
    public virtual TblEventType? EventType { get; set; }
}
