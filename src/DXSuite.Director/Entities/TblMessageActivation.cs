using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessageActivation")]
[Index(nameof(MessageId), Name = "IX_tblMessageActivation_MessageId")]
[Index(nameof(StartUtc), nameof(EndUtc), Name = "IX_tblMessageActivation_Window")]
public partial class TblMessageActivation
{
    [Key]
    public int MessageActivationId { get; set; }

    public int MessageId { get; set; }

    public DateTime StartUtc { get; set; }

    /// <summary>Null means the message stays up until cancelled.</summary>
    public DateTime? EndUtc { get; set; }

    public int? PriorityOverride { get; set; }

    public int? DisplaySecondsOverride { get; set; }

    public int? HideSecondsOverride { get; set; }

    public MessageActivationSource Source { get; set; }

    public int? ActivatedByUserId { get; set; }

    public DateTime ActivatedUtc { get; set; }

    public int? CancelledByUserId { get; set; }

    public DateTime? CancelledUtc { get; set; }

    /// <summary>Texts and sign video path as sent, so the audit record survives later edits to the message.</summary>
    public string? ContentSnapshot { get; set; }

    [ForeignKey("MessageId")]
    [InverseProperty("Activations")]
    public virtual TblMessage Message { get; set; } = null!;

    [InverseProperty("MessageActivation")]
    public virtual ICollection<TblMessageActivationTarget> Targets { get; set; } = new List<TblMessageActivationTarget>();
}
