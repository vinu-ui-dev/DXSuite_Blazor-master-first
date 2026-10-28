using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessage")]
[Index(nameof(MessageTypeId), Name = "IX_tblMessage_MessageTypeId")]
[Index(nameof(OwningUserGroupId), Name = "IX_tblMessage_OwningUserGroupId")]
public partial class TblMessage
{
    [Key]
    public int MessageId { get; set; }

    public int MessageTypeId { get; set; }

    /// <summary>Null means the message is shared by every group.</summary>
    public int? OwningUserGroupId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = null!;

    public bool IsCanned { get; set; }

    /// <summary>Null falls back to the message type's default.</summary>
    [StringLength(9)]
    [Unicode(false)]
    public string? TextColor { get; set; }

    /// <summary>Null falls back to the message type's default.</summary>
    [StringLength(9)]
    [Unicode(false)]
    public string? BackgroundColor { get; set; }

    public MessageStatus Status { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// The approved revision the Message's content is at: 0 as first approved, one more for each approved revision.
    /// It is the n of a shown version's "revision n" (AUD-06).
    /// </summary>
    public int RevisionNumber { get; set; }

    public DateTime CreatedUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? ModifiedUtc { get; set; }

    public int? ModifiedByUserId { get; set; }

    [ForeignKey("MessageTypeId")]
    [InverseProperty("Messages")]
    public virtual TblMessageType MessageType { get; set; } = null!;

    [ForeignKey("OwningUserGroupId")]
    public virtual TblUserGroup? OwningUserGroup { get; set; }

    [InverseProperty("Message")]
    public virtual ICollection<TblMessageText> Texts { get; set; } = new List<TblMessageText>();

    [InverseProperty("Message")]
    public virtual ICollection<TblMessageAsset> Assets { get; set; } = new List<TblMessageAsset>();

    /// <summary>The edit waiting for approval, if any (MSG-13).</summary>
    [InverseProperty("Message")]
    public virtual TblMessageRevision? PendingRevision { get; set; }

    [InverseProperty("Message")]
    public virtual ICollection<TblMessageActivation> Activations { get; set; } = new List<TblMessageActivation>();
}
