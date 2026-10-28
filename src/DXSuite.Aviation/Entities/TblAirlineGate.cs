using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[PrimaryKey("Gat", "AirlineCode")]
[Table("tblAirlineGate")]
public partial class TblAirlineGate
{
    [Key]
    [Column("GAT")]
    [StringLength(8)]
    [Unicode(false)]
    public string Gat { get; set; } = null!;

    [Key]
    [Column("Airline_Code")]
    [StringLength(3)]
    [Unicode(false)]
    public string AirlineCode { get; set; } = null!;

    [StringLength(500)]
    [Unicode(false)]
    public string? WebLink { get; set; }

    [Column("DisableGIDS")]
    public bool DisableGids { get; set; }

    [ForeignKey("AirlineCode")]
    [InverseProperty("TblAirlineGates")]
    public virtual TblAirline AirlineCodeNavigation { get; set; } = null!;

    [ForeignKey("Gat")]
    [InverseProperty("TblAirlineGates")]
    public virtual TblGate GatNavigation { get; set; } = null!;
}
