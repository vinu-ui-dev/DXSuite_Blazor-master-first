using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblBaggageTerminal")]
public partial class TblBaggageTerminal
{
    [Key]
    [Column("DeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string? Description { get; set; }

    [Column("CarouselID")]
    [StringLength(20)]
    [Unicode(false)]
    public string? CarouselId { get; set; }

    [StringLength(1)]
    [Unicode(false)]
    public string Orientation { get; set; } = null!;

    public bool? ShowHelpMessaging { get; set; }

    [StringLength(1)]
    [Unicode(false)]
    public string HelpMessageStyle { get; set; } = null!;

    public bool? ShowMetrics { get; set; }

    public bool? ShowWeather { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime LastSync { get; set; }

    public bool PulseEnabled { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("CarouselId")]
    [InverseProperty("TblBaggageTerminals")]
    public virtual TblCarousel? Carousel { get; set; }
}
