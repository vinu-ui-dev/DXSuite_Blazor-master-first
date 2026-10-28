using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblRCPCode")]
public partial class TblRcpcode
{
    [Key]
    [Column("RCPCode")]
    [StringLength(3)]
    [Unicode(false)]
    public string Rcpcode { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string? Description { get; set; }
}
