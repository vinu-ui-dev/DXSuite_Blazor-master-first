using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCuppsLockout")]
public partial class TblCuppsLockout
{
    [Key]
    public long CuppsLockoutId { get; set; }

    public int UserId { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string CounterId { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime LockedUtc { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime LockedUntilUtc { get; set; }
}
