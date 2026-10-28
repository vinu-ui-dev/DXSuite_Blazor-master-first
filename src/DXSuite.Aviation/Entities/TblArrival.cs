using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblArrivals")]
[Index("FlightRecordId", "Type", Name = "IX_tblArrivals", IsUnique = true)]
public partial class TblArrival
{
    [Key]
    [Column("FlightRecordID")]
    public int FlightRecordId { get; set; }

    [Column("TYPE")]
    [StringLength(1)]
    [Unicode(false)]
    public string Type { get; set; } = null!;

    [Column("FLC")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flc { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Carrier { get; set; }

    [Column("FLN")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Fln { get; set; }

    [Column("FLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flt { get; set; }

    [Column("STA", TypeName = "datetime")]
    public DateTime? Sta { get; set; }

    [Column("ETA", TypeName = "datetime")]
    public DateTime? Eta { get; set; }

    [Column("ATA", TypeName = "datetime")]
    public DateTime? Ata { get; set; }

    [Column("RCP")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Rcp { get; set; }

    [Column("BST")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Bst { get; set; }

    [Column("CAN")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Can { get; set; }

    [Column("MFF")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Mff { get; set; }

    [Column("TOF")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Tof { get; set; }

    [Column("NAT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Nat { get; set; }

    [Column("TEF")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Tef { get; set; }

    [Column("ROP")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Rop { get; set; }

    [Column("ROB")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Rob { get; set; }

    [Column("GAT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Gat { get; set; }

    [Column("PLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Plt { get; set; }

    [Column("CAR")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Car { get; set; }

    [Column("CPR")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Cpr { get; set; }

    [Column("CTY")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Cty { get; set; }

    [Column("ORG")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Org { get; set; }

    [Column("VI1")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Vi1 { get; set; }

    [Column("VI2")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Vi2 { get; set; }

    [Column("VI3")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Vi3 { get; set; }

    [Column("VI4")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Vi4 { get; set; }

    [Column("VI5")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Vi5 { get; set; }

    [Column("VI1DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi1des { get; set; }

    [Column("VI2DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi2des { get; set; }

    [Column("VI3DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi3des { get; set; }

    [Column("VI4DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi4des { get; set; }

    [Column("VI5DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi5des { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? AircraftType { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastModifiedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }
}
