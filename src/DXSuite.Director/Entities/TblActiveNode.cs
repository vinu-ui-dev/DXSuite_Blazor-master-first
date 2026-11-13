using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblActiveNode")]
public partial class TblActiveNode
{
    [Key]
    [StringLength(100)]
    [Unicode(false)]
    public string Scope { get; set; } = null!;

    [StringLength(200)]
    [Unicode(false)]
    public string? HolderNode { get; set; }

    [StringLength(200)]
    [Unicode(false)]
    public string? HolderInstance { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LeaseExpiresUtc { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? AcquiredUtc { get; set; }

    public int? FailbackRequestedBy { get; set; }
}
