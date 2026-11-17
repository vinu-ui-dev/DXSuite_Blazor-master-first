using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblDcsProvider", Schema = "aviation")]
public partial class TblDcsProvider
{
    [Key]
    public int DcsProviderId { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    public int? UserGroupId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? RetiredUtc { get; set; }
}
