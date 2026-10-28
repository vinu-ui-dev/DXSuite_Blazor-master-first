using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblGate")]
[Index("Ter", "Gat", Name = "IX_tblGate", IsUnique = true)]
public partial class TblGate
{
    [Key]
    [Column("GAT")]
    [StringLength(8)]
    [Unicode(false)]
    public string Gat { get; set; } = null!;

    [Column("TER")]
    [StringLength(8)]
    [Unicode(false)]
    public string Ter { get; set; } = null!;

    [Column("ORIENTATION")]
    [StringLength(1)]
    [Unicode(false)]
    public string Orientation { get; set; } = null!;

    [Column("SORT_ORDER")]
    public int? SortOrder { get; set; }

    [Column("DEFAULTSTYLE")]
    [StringLength(1)]
    [Unicode(false)]
    public string Defaultstyle { get; set; } = null!;

    [InverseProperty("GatNavigation")]
    public virtual ICollection<TblAirlineGate> TblAirlineGates { get; set; } = new List<TblAirlineGate>();
}
