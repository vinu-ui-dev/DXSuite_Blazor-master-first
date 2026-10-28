using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessagePermission")]
public partial class TblMessagePermission
{
    [Key]
    public int MessagePermissionId { get; set; }

    public int UserGroupId { get; set; }

    public int MessageTypeId { get; set; }

    /// <summary>Null grants the message type on every zone.</summary>
    public int? ZoneId { get; set; }

    public bool CanFreeText { get; set; }

    [ForeignKey("UserGroupId")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;

    [ForeignKey("MessageTypeId")]
    public virtual TblMessageType MessageType { get; set; } = null!;

    [ForeignKey("ZoneId")]
    public virtual TblZone? Zone { get; set; }
}
