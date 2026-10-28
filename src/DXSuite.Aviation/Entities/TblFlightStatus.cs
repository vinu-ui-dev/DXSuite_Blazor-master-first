using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblFlightStatus")]
public partial class TblFlightStatus
{
    [Key]
    [Column("StatusID")]
    [StringLength(20)]
    [Unicode(false)]
    public string StatusId { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string StatusName { get; set; } = null!;

    public bool IsLanded { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }
}
