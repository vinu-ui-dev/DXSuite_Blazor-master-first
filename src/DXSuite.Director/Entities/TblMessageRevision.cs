using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// The pending revision of an Approved Message (MSG-13): the whole proposed content, held apart so screens keep the
/// approved text until the type's chain approves it. A Message has at most one; a second edit replaces it, approval
/// copies it onto the Message and rejection discards it. MessageRevisionId is the <c>revisionId</c> of the
/// message.revision-submitted, message.approved and message.rejected events.
/// </summary>
[Table("tblMessageRevision")]
[Index(nameof(MessageId), IsUnique = true, Name = "UX_tblMessageRevision_MessageId")]
public partial class TblMessageRevision
{
    [Key]
    public int MessageRevisionId { get; set; }

    public int MessageId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>Null falls back to the message type's default.</summary>
    [StringLength(9)]
    [Unicode(false)]
    public string? TextColor { get; set; }

    /// <summary>Null falls back to the message type's default.</summary>
    [StringLength(9)]
    [Unicode(false)]
    public string? BackgroundColor { get; set; }

    public DateTime SubmittedUtc { get; set; }

    public int? SubmittedByUserId { get; set; }

    [ForeignKey("MessageId")]
    [InverseProperty("PendingRevision")]
    public virtual TblMessage Message { get; set; } = null!;

    [InverseProperty("MessageRevision")]
    public virtual ICollection<TblMessageRevisionText> Texts { get; set; } = new List<TblMessageRevisionText>();

    [InverseProperty("MessageRevision")]
    public virtual ICollection<TblMessageRevisionAsset> Assets { get; set; } = new List<TblMessageRevisionAsset>();
}
