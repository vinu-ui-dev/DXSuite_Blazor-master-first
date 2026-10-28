using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[PrimaryKey("Origin", "Destination")]
[Table("tblSpacialDisposition")]
public partial class TblSpacialDisposition
{
    [Key]
    [StringLength(50)]
    [Unicode(false)]
    public string Origin { get; set; } = null!;

    [Key]
    [StringLength(50)]
    [Unicode(false)]
    public string Destination { get; set; } = null!;

    public int? Direction { get; set; }

    public int? TimeEstimateMinutes { get; set; }
}
