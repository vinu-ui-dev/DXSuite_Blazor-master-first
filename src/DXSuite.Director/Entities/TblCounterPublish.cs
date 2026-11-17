using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterPublish", Schema = "aviation")]
public partial class TblCounterPublish
{
    [Key]
    public long PublishId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime SentUtc { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? ActorKind { get; set; }

    public int? UserId { get; set; }

    [StringLength(200)]
    public string? ActorName { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? AccountKind { get; set; }

    [StringLength(200)]
    public string? PersonName { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? StationCounterId { get; set; }

    public int? ActingForAirlineId { get; set; }

    [StringLength(200)]
    public string? ActingForName { get; set; }

    public int? CounterTemplateId { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string? CounterTemplateKind { get; set; }

    [StringLength(200)]
    public string? CounterTemplateName { get; set; }

    public bool? Sunflower { get; set; }

    [StringLength(200)]
    public string? Destination { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? SystemReason { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FirstServedUtc { get; set; }

    public int? PageShowSeconds { get; set; }
}
