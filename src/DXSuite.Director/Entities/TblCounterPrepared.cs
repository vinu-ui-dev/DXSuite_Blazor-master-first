using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterPrepared", Schema = "aviation")]
public partial class TblCounterPrepared
{
    [Key]
    public long CounterPreparedId { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string CounterId { get; set; } = null!;

    public int AirlineId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime AllocationStartUtc { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime AllocationEndUtc { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime PreparedUtc { get; set; }

    public int? PreparedByUserId { get; set; }

    public int? CounterTemplateId { get; set; }

    public bool? Sunflower { get; set; }

    public bool? AslOn { get; set; }

    [StringLength(200)]
    public string? Destination { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? SplitChoice { get; set; }

    public string? Messages { get; set; }

    public long? PublishId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ResolvedUtc { get; set; }
}
