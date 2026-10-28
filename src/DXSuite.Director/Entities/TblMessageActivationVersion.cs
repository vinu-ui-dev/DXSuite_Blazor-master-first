using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// A shown version of an Activation (FSD 1.1 AUD-06): what it showed from the send, or from the approval of
/// a revision, until the next version. Immutable once written, except <see cref="FirstServedUtc"/>, and never
/// deleted, so the Message Playback Log and User events can point at it. Its id is the <c>VersionId</c> the
/// delivery route publishes. Message Type and assets are copied rather than referenced because either may
/// later be edited or deleted.
/// </summary>
[Table("tblMessageActivationVersion")]
[Index(nameof(MessageActivationId), nameof(Sequence), IsUnique = true, Name = "UX_tblMessageActivationVersion_Activation_Sequence")]
public partial class TblMessageActivationVersion
{
    [Key]
    public int MessageActivationVersionId { get; set; }

    public int MessageActivationId { get; set; }

    /// <summary>1 for the first send, then one more per approved revision.</summary>
    public int Sequence { get; set; }

    public ShownVersionReason Reason { get; set; }

    /// <summary>Which revision of the Message was approved; null for the first send.</summary>
    public int? RevisionNumber { get; set; }

    public int MessageTypeId { get; set; }

    [Required]
    [StringLength(30)]
    [Unicode(false)]
    public string MessageTypeCode { get; set; } = null!;

    /// <summary>The Activation's effective rank when this version was sent (MSG-09).</summary>
    public int EffectiveRank { get; set; }

    /// <summary>JSON object of text by language code, such as <c>{"en":"…","fr":"…"}</c>.</summary>
    [Required]
    public string Texts { get; set; } = null!;

    /// <summary>
    /// JSON array with one entry per Asset Slot filled: <c>slotKey</c>, <c>assetId</c>, <c>fileName</c> and
    /// <c>contentHash</c>. An empty array when the Message has no assets.
    /// </summary>
    [Required]
    public string AssetSlots { get; set; } = null!;

    /// <summary>Sent: the send, or the approval of the revision.</summary>
    public DateTime SentUtc { get; set; }

    /// <summary>Delivered to signage: stamped once by the first delivery response that includes it (INK-06).</summary>
    public DateTime? FirstServedUtc { get; set; }

    [ForeignKey("MessageActivationId")]
    [InverseProperty("Versions")]
    public virtual TblMessageActivation MessageActivation { get; set; } = null!;
}
