using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>A pending revision's proposed text in one language; the same shape as tblMessageText.</summary>
[Table("tblMessageRevisionText")]
[PrimaryKey(nameof(MessageRevisionId), nameof(LanguageCode))]
public partial class TblMessageRevisionText
{
    public int MessageRevisionId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string LanguageCode { get; set; } = null!;

    [Required]
    [StringLength(500)]
    public string Body { get; set; } = null!;

    [ForeignKey("MessageRevisionId")]
    [InverseProperty("Texts")]
    public virtual TblMessageRevision MessageRevision { get; set; } = null!;
}
