using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblCarousel")]
public partial class TblCarousel
{
    [Key]
    [Column("CarouselID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CarouselId { get; set; } = null!;

    public int Terminal { get; set; }

    public int? SortOrder { get; set; }

    public bool Output { get; set; }

    [Column("HideCarouselID")]
    public bool HideCarouselId { get; set; }

    public bool BeltRelayEnabled { get; set; }

    [Column("BeltRelayIP")]
    [StringLength(50)]
    [Unicode(false)]
    public string BeltRelayIp { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string BeltRelayCredentials { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string? RelayInputState { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? RelayInputLastSync { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [InverseProperty("Carousel")]
    public virtual ICollection<TblBaggageTerminal> TblBaggageTerminals { get; set; } = new List<TblBaggageTerminal>();

    [InverseProperty("CarNavigation")]
    public virtual ICollection<TblFlight> TblFlights { get; set; } = new List<TblFlight>();
}
