using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblContentHealthEvent")]
public class TblContentHealthEvent
{
    [Key]
    public int EventId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    [StringLength(500)]
    public string? ContentUrl { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? PreviousStatus { get; set; }

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [StringLength(200)]
    public string? Reason { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime EventTime { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ReceivedTime { get; set; }

    [ForeignKey("DeviceId")]
    public virtual TblDevice? Device { get; set; }
}
