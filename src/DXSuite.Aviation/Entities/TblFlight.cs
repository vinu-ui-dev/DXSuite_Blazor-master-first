using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblFlights")]
public partial class TblFlight
{
    [Key]
    [Column("FlightRecordID")]
    public int FlightRecordId { get; set; }

    [Column("FLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flt { get; set; }

    [Column("FLC")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Flc { get; set; }

    [Column("FLN")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Fln { get; set; }

    [Column("ORG")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Org { get; set; }

    [Column("STA", TypeName = "datetime")]
    public DateTime? Sta { get; set; }

    [Column("ETA", TypeName = "datetime")]
    public DateTime? Eta { get; set; }

    [Column("ATA", TypeName = "datetime")]
    public DateTime? Ata { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FirstBag { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastBag { get; set; }

    [Column("RCP")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Rcp { get; set; }

    [Column("BHSCAR")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Bhscar { get; set; }

    [Column("CAR")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Car { get; set; }

    [Column("CPR")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Cpr { get; set; }

    [Column("GAT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Gat { get; set; }

    [Column("TEF")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Tef { get; set; }

    public bool LastBagDelayActive { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? AircraftType { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastModifiedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [ForeignKey("Car")]
    [InverseProperty("TblFlights")]
    public virtual TblCarousel? CarNavigation { get; set; }

    [ForeignKey("Flc")]
    [InverseProperty("TblFlights")]
    public virtual TblAirline? FlcNavigation { get; set; }
}
