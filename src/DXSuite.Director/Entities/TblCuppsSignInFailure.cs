using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCuppsSignInFailure")]
public partial class TblCuppsSignInFailure
{
    [Key]
    public long CuppsSignInFailureId { get; set; }

    public int UserId { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string CounterId { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime FailedUtc { get; set; }
}
