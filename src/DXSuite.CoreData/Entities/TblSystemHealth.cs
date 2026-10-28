using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

[Table("tblSystemHealth")]
public partial class TblSystemHealth
{
    [Key]
    [Column("RecordID")]
    public int RecordId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Source { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string? Description { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string? Value { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? ErrorCriteria { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastModified { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }
}
