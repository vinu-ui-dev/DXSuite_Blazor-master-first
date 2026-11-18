using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterPublishTarget", Schema = "aviation")]
[PrimaryKey(nameof(PublishId), nameof(CounterId), nameof(DisplayId), nameof(Half))]
public partial class TblCounterPublishTarget
{
    public int PublishId { get; set; }

    public string CounterId { get; set; } = null!;

    public int DisplayId { get; set; }

    public string DisplayName { get; set; } = null!;

    public string Half { get; set; } = null!;

    [Column(TypeName = "datetime2")]
    public DateTime? UntilUtc { get; set; }

    public string? EndReason { get; set; }

    public int? ReplacedByPublishId { get; set; }
}
