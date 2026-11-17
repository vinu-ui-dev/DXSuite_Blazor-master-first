using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterTemplate", Schema = "aviation")]
public partial class TblCounterTemplate
{
    [Key]
    public int CounterTemplateId { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    public int AirlineId { get; set; }

    public byte Kind { get; set; }

    public byte? ImageLayout { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime StartDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? EndDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? RetiredUtc { get; set; }

    [InverseProperty(nameof(TblCounterTemplateAsset.CounterTemplate))]
    public virtual ICollection<TblCounterTemplateAsset> Assets { get; set; } = new List<TblCounterTemplateAsset>();

    [InverseProperty(nameof(TblCounterTemplateReplacement.CounterTemplate))]
    public virtual ICollection<TblCounterTemplateReplacement> Replacements { get; set; } = new List<TblCounterTemplateReplacement>();

    [InverseProperty(nameof(TblCounterTemplateText.CounterTemplate))]
    public virtual ICollection<TblCounterTemplateText> Texts { get; set; } = new List<TblCounterTemplateText>();

    [InverseProperty(nameof(TblCounterTemplateOutcome.CounterTemplate))]
    public virtual TblCounterTemplateOutcome? Outcome { get; set; }
}
