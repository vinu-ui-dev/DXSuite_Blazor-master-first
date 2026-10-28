using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// The asset chosen for one slot of a message. SlotKey matches a slot configured on the message's type, and
/// the asset's own ConstraintId must be that slot's library.
/// </summary>
[Table("tblMessageAsset")]
[PrimaryKey(nameof(MessageId), nameof(SlotKey))]
[Index(nameof(AssetId), Name = "IX_tblMessageAsset_AssetId")]
public partial class TblMessageAsset
{
    public int MessageId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string SlotKey { get; set; } = null!;

    public int AssetId { get; set; }

    [ForeignKey("MessageId")]
    [InverseProperty("Assets")]
    public virtual TblMessage Message { get; set; } = null!;

    [ForeignKey("AssetId")]
    public virtual TblAsset Asset { get; set; } = null!;
}
