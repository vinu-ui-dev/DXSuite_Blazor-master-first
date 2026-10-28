using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Keyless]
public partial class ViewBhsLastBagExpiredReport
{
    [Column("FlightRecordID")]
    public int? FlightRecordId { get; set; }

    [Column("DeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    [StringLength(8)]
    [Unicode(false)]
    public string? CurrentCarousel { get; set; }

    [Column("FLN")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Fln { get; set; }

    [Column("STA", TypeName = "datetime")]
    public DateTime? Sta { get; set; }

    [Column("ATA", TypeName = "datetime")]
    public DateTime? Ata { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FirstBag { get; set; }

    public int? FirstBagTime { get; set; }

    public int? UnloadTime { get; set; }

    public int? TimeComplete { get; set; }

    [Unicode(false)]
    public string? EventMessage { get; set; }
}
