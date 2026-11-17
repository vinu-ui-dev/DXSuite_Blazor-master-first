using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAllianceLogo", Schema = "aviation")]
[PrimaryKey(nameof(AllianceId), nameof(RoleKey))]
public partial class TblAllianceLogo
{
    public int AllianceId { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string RoleKey { get; set; } = null!;

    public int AssetId { get; set; }

    public bool IsLive { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LinkedUtc { get; set; }
}
