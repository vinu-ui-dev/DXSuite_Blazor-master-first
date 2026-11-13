using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblBusinessRuleValue")]
public partial class TblBusinessRuleValue
{
    [Key]
    [Column("Key")]
    [StringLength(100)]
    public string BusinessRuleKey { get; set; } = null!;

    [Column("Value")]
    public string? BusinessRuleValueText { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ChangedUtc { get; set; }

    public int? ChangedByUserId { get; set; }
}
