using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Per-role logo content for an airline on a wall. Each row supplies the content for
/// logo zones assigned to one named role (VideoWallConfiguration.LogoRoles); zones
/// whose LogoRoleKey is null keep resolving to the parent row's legacy LogoPath,
/// so airlines without per-role uploads render exactly as before.
/// </summary>
[Table("tblUserGroupLocationLogo")]
[Index("UserGroupLocationDefaultId", "LogoRoleKey", IsUnique = true, Name = "UX_tblUserGroupLocationLogo_DefaultRole")]
public class TblUserGroupLocationLogo
{
    [Key]
    public int UserGroupLocationLogoId { get; set; }

    public int UserGroupLocationDefaultId { get; set; }

    /// <summary>Key of the wall's LogoRoleDefinition this content renders in.</summary>
    [StringLength(64)]
    [Unicode(false)]
    public string LogoRoleKey { get; set; } = "";

    /// <summary>
    /// URL or uploaded-file path, same conventions as TblUserGroupLocationDefault.LogoPath:
    /// http(s) URLs, uploaded image/svg/video files, or HTML served via /videowall/html-asset.
    /// </summary>
    [StringLength(500)]
    public string? ContentPath { get; set; }

    /// <summary>Render mode for image/video content (see LayerRenderMode for semantics).</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string ContentMode { get; set; } = "fit";

    [Column(TypeName = "datetime2")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime LastEditDate { get; set; }

    [ForeignKey("UserGroupLocationDefaultId")]
    public virtual TblUserGroupLocationDefault UserGroupLocationDefault { get; set; } = null!;
}
