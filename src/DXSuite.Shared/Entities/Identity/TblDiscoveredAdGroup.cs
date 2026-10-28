using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblDiscoveredAdGroup")]
[Index(nameof(ObjectId), IsUnique = true)]
public partial class TblDiscoveredAdGroup
{
    [Key]
    public int DiscoveredAdGroupId { get; set; }

    /// <summary>
    /// Azure AD Object ID (GUID) for the group
    /// </summary>
    [StringLength(36)]
    [Unicode(false)]
    public string ObjectId { get; set; } = null!;

    /// <summary>
    /// Display name resolved from Microsoft Graph
    /// </summary>
    [StringLength(200)]
    [Unicode(false)]
    public string? DisplayName { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FirstSeenDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime LastSeenDate { get; set; }

    /// <summary>
    /// Admin dismissed this group without adopting it
    /// </summary>
    public bool IsDismissed { get; set; }

    /// <summary>
    /// Group has been adopted into a Director user group
    /// </summary>
    public bool IsAdopted { get; set; }

    /// <summary>
    /// FK to the adopted TblUserGroup (set when adopted)
    /// </summary>
    public int? AdoptedUserGroupId { get; set; }

    [ForeignKey("AdoptedUserGroupId")]
    public virtual TblUserGroup? AdoptedUserGroup { get; set; }
}
