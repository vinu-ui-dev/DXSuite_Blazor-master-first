using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One ordered side-by-side segment of an airline's background or mask layer.
/// When any rows exist for a (default, LayerRole) pair the wall renders the ordered
/// segment sequence — each at its native width, continuing at the previous segment's
/// right edge and wrapping back to the first until the cluster is covered — instead
/// of the single MaskPath/BackgroundPath on the parent row (which remains the
/// legacy single-file path).
/// </summary>
[Table("tblUserGroupLocationLayerContent")]
[Index("UserGroupLocationDefaultId", "LayerRole", "SortOrder", IsUnique = true, Name = "UX_tblUserGroupLocationLayerContent_DefaultRoleOrder")]
public class TblUserGroupLocationLayerContent
{
    [Key]
    public int UserGroupLocationLayerContentId { get; set; }

    public int UserGroupLocationDefaultId { get; set; }

    /// <summary>"background" or "mask".</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string LayerRole { get; set; } = "background";

    /// <summary>Zero-based position in the side-by-side sequence.</summary>
    public int SortOrder { get; set; }

    /// <summary>Uploaded-file path or URL, same conventions as MaskPath/BackgroundPath.</summary>
    [StringLength(500)]
    public string? ContentPath { get; set; }

    /// <summary>
    /// Native pixel width used for side-by-side placement, probed with ffprobe at
    /// upload (editable in the UI for URL content ffprobe cannot reach). Zero means
    /// unknown — the segment fills the remaining cluster width and ends the sequence.
    /// </summary>
    public int IntrinsicWidthPx { get; set; }

    /// <summary>Render mode reserved for future use; segments render pixel-exact today.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string ContentMode { get; set; } = "native";

    [Column(TypeName = "datetime2")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime LastEditDate { get; set; }

    [ForeignKey("UserGroupLocationDefaultId")]
    public virtual TblUserGroupLocationDefault UserGroupLocationDefault { get; set; } = null!;
}
