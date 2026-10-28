using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// The asset a pending revision proposes for one slot of its type, such as a new ASL video; the same shape as
/// tblMessageAsset. One approval covers the texts and these assets together (MSG-11).
/// </summary>
[Table("tblMessageRevisionAsset")]
[PrimaryKey(nameof(MessageRevisionId), nameof(SlotKey))]
[Index(nameof(AssetId), Name = "IX_tblMessageRevisionAsset_AssetId")]
public partial class TblMessageRevisionAsset
{
    public int MessageRevisionId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string SlotKey { get; set; } = null!;

    public int AssetId { get; set; }

    [ForeignKey("MessageRevisionId")]
    [InverseProperty("Assets")]
    public virtual TblMessageRevision MessageRevision { get; set; } = null!;

    [ForeignKey("AssetId")]
    public virtual TblAsset Asset { get; set; } = null!;
}
