using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterTemplateOutcome", Schema = "aviation")]
[PrimaryKey(nameof(CounterTemplateId))]
public partial class TblCounterTemplateOutcome
{
    public int CounterTemplateId { get; set; }

    public int? FirstApprovalRequestId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? SentUtc { get; set; }

    [ForeignKey(nameof(CounterTemplateId))]
    [InverseProperty(nameof(TblCounterTemplate.Outcome))]
    public virtual TblCounterTemplate CounterTemplate { get; set; } = null!;
}
