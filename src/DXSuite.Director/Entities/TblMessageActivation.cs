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

    /// <summary>
    /// When the Activation starts. For a scheduled send it is the earliest instant <see cref="StartLocal"/> reached on
    /// any of its screens at send, so UTC queries ("active or scheduled", "ended everywhere") stay correct.
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Null means the message stays up until cancelled. For a scheduled send it is the latest instant
    /// <see cref="EndLocal"/> reached on any of its screens at send.
    /// </summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>
    /// A scheduled send's date range as wall-clock bounds (start inclusive, end exclusive), read in each display's own
    /// time zone (MSG-16). Both null for Send now, which runs on <see cref="StartUtc"/> and <see cref="EndUtc"/>.
    /// </summary>
    public DateTime? StartLocal { get; set; }

    public DateTime? EndLocal { get; set; }

    public int? PriorityOverride { get; set; }

    public int? DisplaySecondsOverride { get; set; }

    public int? HideSecondsOverride { get; set; }

    // The weekly window of a scheduled send (MSG-16): the days it shows on and, when both times are set, the daily
    // window as wall-clock time in each display's own time zone. Every day with no times is no weekly window, which
    // is also what Send now stores.
    public bool Sunday { get; set; } = true;
    public bool Monday { get; set; } = true;
    public bool Tuesday { get; set; } = true;
    public bool Wednesday { get; set; } = true;
    public bool Thursday { get; set; } = true;
    public bool Friday { get; set; } = true;
    public bool Saturday { get; set; } = true;

    public TimeOnly? WindowStartTime { get; set; }

    public TimeOnly? WindowEndTime { get; set; }

    public MessageActivationSource Source { get; set; }

    public int? ActivatedByUserId { get; set; }

    public DateTime ActivatedUtc { get; set; }

    public int? CancelledByUserId { get; set; }

    public DateTime? CancelledUtc { get; set; }

    /// <summary>
    /// When <c>activation.ended</c> was logged for this Activation (AUD-08), so the job that logs it, on either
    /// instance, never logs it twice. Activations already over when the column was added are marked with their end.
    /// </summary>
    public DateTime? EndedLoggedUtc { get; set; }

    [ForeignKey("MessageId")]
    [InverseProperty("Activations")]
    public virtual TblMessage Message { get; set; } = null!;

    [InverseProperty("MessageActivation")]
    public virtual ICollection<TblMessageActivationTarget> Targets { get; set; } = new List<TblMessageActivationTarget>();

    /// <summary>The Display Zones the sender picked; none for a one-display send.</summary>
    [InverseProperty("MessageActivation")]
    public virtual ICollection<TblMessageActivationZone> ChosenZones { get; set; } = new List<TblMessageActivationZone>();

    /// <summary>What the Activation put on screen, as sent and as each approved revision replaced it.</summary>
    [InverseProperty("MessageActivation")]
    public virtual ICollection<TblMessageActivationVersion> Versions { get; set; } = new List<TblMessageActivationVersion>();
}
