using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One asset slot on a message type, fed by a global constraint library. SlotKey names the role the asset
/// plays, so two slots may draw from the same library and the datapipe export keeps stable columns.
/// </summary>
[Table("tblMessageTypeAssetLibrary")]
[Index(nameof(MessageTypeId), nameof(SlotKey), IsUnique = true, Name = "UX_tblMessageTypeAssetLibrary_Type_Slot")]
[Index(nameof(ConstraintId), Name = "IX_tblMessageTypeAssetLibrary_ConstraintId")]
public partial class TblMessageTypeAssetLibrary
{
    [Key]
    public int MessageTypeAssetLibraryId { get; set; }

    public int MessageTypeId { get; set; }

    public int ConstraintId { get; set; }

    [Required]
    [StringLength(30)]
    [Unicode(false)]
    public string SlotKey { get; set; } = null!;

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    [ForeignKey("MessageTypeId")]
    [InverseProperty("AssetLibraries")]
    public virtual TblMessageType MessageType { get; set; } = null!;

    [ForeignKey("ConstraintId")]
    public virtual TblConstraint Constraint { get; set; } = null!;
}
