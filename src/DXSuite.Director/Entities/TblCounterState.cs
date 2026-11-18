using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterState", Schema = "aviation")]
public partial class TblCounterState
{
    [Key]
    [StringLength(50)]
    [Unicode(false)]
    public string CounterId { get; set; } = null!;

    public int HolderAirlineId { get; set; }

    public bool Open { get; set; }

    public int? CounterTemplateId { get; set; }

    public byte? CounterTemplateKind { get; set; }

    public bool Sunflower { get; set; }

    public bool AslOn { get; set; }

    [StringLength(60)]
    public string? Destination { get; set; }

    public byte SplitChoice { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? AllocationEndUtc { get; set; }

    public byte? AllocationCounterType { get; set; }

    [StringLength(100)]
    public string? AllocationFlightNumber { get; set; }

    [StringLength(100)]
    public string? AllocationDestination { get; set; }

    public int? LastPublishId { get; set; }

    public int? FirstMessageActivationId { get; set; }

    public int? SecondMessageActivationId { get; set; }
}
