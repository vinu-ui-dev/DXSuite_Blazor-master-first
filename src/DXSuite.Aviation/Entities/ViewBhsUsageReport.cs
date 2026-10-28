using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Keyless]
public partial class ViewBhsUsageReport
{
    [Column("FLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flt { get; set; }

    [Column("ORG")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Org { get; set; }

    [Column("STA", TypeName = "datetime")]
    public DateTime? Sta { get; set; }

    [Column("ATA", TypeName = "datetime")]
    public DateTime? Ata { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FirstBag { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastBag { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? MovedToCarousel { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? SitaFeedCarousel { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? OriginalCarousel { get; set; }

    public int? FirstBagTime { get; set; }

    public int? UnloadTime { get; set; }

    public int? TimeComplete { get; set; }
}
