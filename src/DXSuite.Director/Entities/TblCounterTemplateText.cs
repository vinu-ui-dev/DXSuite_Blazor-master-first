using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterTemplateText", Schema = "aviation")]
[PrimaryKey(nameof(CounterTemplateId), nameof(LanguageCode))]
public partial class TblCounterTemplateText
{
    public int CounterTemplateId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string LanguageCode { get; set; } = null!;

    [StringLength(500)]
    public string Body { get; set; } = null!;

    [ForeignKey(nameof(CounterTemplateId))]
    [InverseProperty(nameof(TblCounterTemplate.Texts))]
    public virtual TblCounterTemplate CounterTemplate { get; set; } = null!;
}
