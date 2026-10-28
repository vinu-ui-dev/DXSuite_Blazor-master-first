using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblUserNotificationPreference")]
public partial class TblUserNotificationPreference
{
    [Key]
    public int UserNotificationPreferenceId { get; set; }

    public int UserId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string Category { get; set; } = null!;

    public bool EmailEnabled { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime LastEditDate { get; set; }

    [ForeignKey("UserId")]
    public virtual TblUser User { get; set; } = null!;
}
