using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>Exactly one of ZoneId, DisplayId or LocationId is set, enforced by a check constraint.</summary>
[Table("tblMessageActivationTarget")]
[Index(nameof(MessageActivationId), Name = "IX_tblMessageActivationTarget_MessageActivationId")]
[Index(nameof(ZoneId), Name = "IX_tblMessageActivationTarget_ZoneId")]
[Index(nameof(DisplayId), Name = "IX_tblMessageActivationTarget_DisplayId")]
[Index(nameof(LocationId), Name = "IX_tblMessageActivationTarget_LocationId")]
public partial class TblMessageActivationTarget
{
    [Key]
    public int MessageActivationTargetId { get; set; }

    public int MessageActivationId { get; set; }

    public int? ZoneId { get; set; }

    public int? DisplayId { get; set; }

    public int? LocationId { get; set; }

    [ForeignKey("MessageActivationId")]
    [InverseProperty("Targets")]
    public virtual TblMessageActivation MessageActivation { get; set; } = null!;

    [ForeignKey("ZoneId")]
    public virtual TblZone? Zone { get; set; }

    [ForeignKey("DisplayId")]
    public virtual TblDisplay? Display { get; set; }

    [ForeignKey("LocationId")]
    public virtual TblLocation? Location { get; set; }
}
