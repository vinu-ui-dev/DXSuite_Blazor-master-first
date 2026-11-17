using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblAlliance", Schema = "aviation")]
public partial class TblAlliance
{
    [Key]
    public int AllianceId { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime? RetiredUtc { get; set; }
}
