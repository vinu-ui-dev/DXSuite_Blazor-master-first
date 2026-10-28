using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Keyless]
public partial class ViewRadGidsFightsAllDay
{
    [Column("TER")]
    [StringLength(8)]
    [Unicode(false)]
    public string Ter { get; set; } = null!;

    [Column("STD", TypeName = "datetime")]
    public DateTime? Std { get; set; }

    [Column("ETD", TypeName = "datetime")]
    public DateTime? Etd { get; set; }

    [Column("ATD", TypeName = "datetime")]
    public DateTime? Atd { get; set; }

    [Column("FLC")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flc { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Carrier { get; set; }

    [Column("GAT")]
    [StringLength(8)]
    [Unicode(false)]
    public string Gat { get; set; } = null!;

    [Column("FLN")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Fln { get; set; }

    [Column("FLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flt { get; set; }

    [Column("RCP")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Rcp { get; set; }

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

    [Column("PLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Plt { get; set; }

    [Column("DES")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Des { get; set; }

    [Column("CTY")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Cty { get; set; }

    [Column("VI1")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Vi1 { get; set; }

    [Column("VI1DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi1des { get; set; }

    [Column("VI2")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Vi2 { get; set; }

    [Column("VI2DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi2des { get; set; }

    [Column("VI3")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Vi3 { get; set; }

    [Column("VI3DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi3des { get; set; }

    [Column("VI4")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Vi4 { get; set; }

    [Column("VI4DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi4des { get; set; }

    [Column("VI5")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Vi5 { get; set; }

    [Column("VI5DES")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Vi5des { get; set; }
}
