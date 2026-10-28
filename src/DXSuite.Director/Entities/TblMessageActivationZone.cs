using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One Display Zone the sender picked for an Activation (ZONE-12, A-33). A display reached through two picked zones
/// gets one screen row, through the first of them (ZONE-06), so a zone whose displays all sit in another picked zone
/// has no screen row of its own; this row still records that the Activation went through it, so the zone counts as
/// targeted and in use like any other.
/// </summary>
[Table("tblMessageActivationZone")]
[PrimaryKey(nameof(MessageActivationId), nameof(ZoneId))]
[Index(nameof(ZoneId), Name = "IX_tblMessageActivationZone_ZoneId")]
public partial class TblMessageActivationZone
{
    public int MessageActivationId { get; set; }

    public int ZoneId { get; set; }

    [ForeignKey("MessageActivationId")]
    [InverseProperty("ChosenZones")]
    public virtual TblMessageActivation MessageActivation { get; set; } = null!;

    [ForeignKey("ZoneId")]
    public virtual TblZone Zone { get; set; } = null!;
}
