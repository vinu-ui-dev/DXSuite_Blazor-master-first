using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCounterTemplateAsset", Schema = "aviation")]
[PrimaryKey(nameof(CounterTemplateId), nameof(SlotKey))]
public partial class TblCounterTemplateAsset
{
    public int CounterTemplateId { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string SlotKey { get; set; } = null!;

    public int AssetId { get; set; }

    [ForeignKey(nameof(CounterTemplateId))]
    [InverseProperty(nameof(TblCounterTemplate.Assets))]
    public virtual TblCounterTemplate CounterTemplate { get; set; } = null!;
}
