using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterState", Schema = "aviation")]
public partial class TblCounterState
{
    [Key]
    [StringLength(32)]
    [Unicode(false)]
    public string CounterId { get; set; } = null!;

    public int? HolderAirlineId { get; set; }

    public bool Open { get; set; }

    public int? CounterTemplateId { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? CounterTemplateKind { get; set; }

    public bool? Sunflower { get; set; }

    public bool? AslOn { get; set; }

    [StringLength(200)]
    public string? Destination { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? SplitChoice { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? AllocationEndUtc { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string? AllocationCounterType { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? AllocationFlightNumber { get; set; }

    [StringLength(200)]
    public string? AllocationDestination { get; set; }

    public long? LastPublishId { get; set; }

    public int? FirstMessageActivationId { get; set; }

    public int? SecondMessageActivationId { get; set; }
}
