using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One display of an Activation's screen list, frozen at send (ZONE-06). DisplayId is always set (check constraint);
/// ZoneId is the Display Zone the display came through, null when the send targeted one display. DisplayId has no
/// foreign key and the row keeps the display's and location's names, so a hard-deleted display's history stays whole.
/// </summary>
[Table("tblMessageActivationTarget")]
[Index(nameof(MessageActivationId), Name = "IX_tblMessageActivationTarget_MessageActivationId")]
[Index(nameof(ZoneId), Name = "IX_tblMessageActivationTarget_ZoneId")]
[Index(nameof(DisplayId), Name = "IX_tblMessageActivationTarget_DisplayId")]
public partial class TblMessageActivationTarget
{
    [Key]
    public int MessageActivationTargetId { get; set; }

    public int MessageActivationId { get; set; }

    public int? ZoneId { get; set; }

    public int? DisplayId { get; set; }

    /// <summary>The display's name at send.</summary>
    [StringLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary>The name of the display's location at send.</summary>
    [StringLength(100)]
    public string LocationName { get; set; } = null!;

    [ForeignKey("MessageActivationId")]
    [InverseProperty("Targets")]
    public virtual TblMessageActivation MessageActivation { get; set; } = null!;

    [ForeignKey("ZoneId")]
    public virtual TblZone? Zone { get; set; }
}
