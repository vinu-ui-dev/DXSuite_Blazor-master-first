using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessageText")]
[PrimaryKey(nameof(MessageId), nameof(LanguageCode))]
public partial class TblMessageText
{
    public int MessageId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string LanguageCode { get; set; } = null!;

    [Required]
    [StringLength(500)]
    public string Body { get; set; } = null!;

    [ForeignKey("MessageId")]
    [InverseProperty("Texts")]
    public virtual TblMessage Message { get; set; } = null!;
}
