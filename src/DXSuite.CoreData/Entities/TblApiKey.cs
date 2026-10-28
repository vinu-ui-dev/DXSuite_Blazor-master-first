using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

[Table("tblApiKey")]
public partial class TblApiKey
{
    [Key]
    [Column("RecordID")]
    public int RecordId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Name { get; set; }

    [StringLength(128)]
    [Unicode(false)]
    public string ApiKey { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string? FeatureFlags { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
