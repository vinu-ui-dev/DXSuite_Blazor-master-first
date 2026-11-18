using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterPublish", Schema = "aviation")]
public partial class TblCounterPublish
{
    [Key]
    public int PublishId { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime SentUtc { get; set; }

    public byte ActorKind { get; set; }

    public int? UserId { get; set; }

    [StringLength(256)]
    public string ActorName { get; set; } = null!;

    public byte? AccountKind { get; set; }

    [StringLength(256)]
    public string? PersonName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? StationCounterId { get; set; }

    public int? ActingForAirlineId { get; set; }

    [StringLength(200)]
    public string? ActingForName { get; set; }

    public int? CounterTemplateId { get; set; }

    public byte? CounterTemplateKind { get; set; }

    [StringLength(50)]
    public string? CounterTemplateName { get; set; }

    public bool Sunflower { get; set; }

    [StringLength(60)]
    public string? Destination { get; set; }

    public byte? SystemReason { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? FirstServedUtc { get; set; }

    public int? PageShowSeconds { get; set; }
}
