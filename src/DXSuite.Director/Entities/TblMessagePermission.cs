using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// A Message access rule (ZONE-07): a user or a group (exactly one, by check constraint) may send the type to one
/// Display Zone or to every Display Zone, with or without one-off text.
/// </summary>
[Table("tblMessagePermission")]
public partial class TblMessagePermission
{
    [Key]
    public int MessagePermissionId { get; set; }

    public int? UserId { get; set; }

    public int? UserGroupId { get; set; }

    public int MessageTypeId { get; set; }

    /// <summary>Null grants the message type on every zone.</summary>
    public int? ZoneId { get; set; }

    public bool CanFreeText { get; set; }

    [ForeignKey("UserId")]
    public virtual TblUser? User { get; set; }

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup? UserGroup { get; set; }

    [ForeignKey("MessageTypeId")]
    public virtual TblMessageType MessageType { get; set; } = null!;

    [ForeignKey("ZoneId")]
    public virtual TblZone? Zone { get; set; }
}
