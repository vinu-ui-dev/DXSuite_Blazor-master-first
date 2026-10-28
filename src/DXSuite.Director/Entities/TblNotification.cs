using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// In-app notification delivered to a user. Supports optional email delivery
/// and click-through navigation to the relevant page.
/// </summary>
[Table("tblNotification")]
public partial class TblNotification
{
    [Key]
    public int NotificationId { get; set; }

    public int UserId { get; set; }

    [StringLength(200)]
    public string Title { get; set; } = null!;

    [StringLength(500)]
    public string? Message { get; set; }

    /// <summary>
    /// "approval-pending", "approval-approved", "approval-declined", "schedule-expiring"
    /// </summary>
    [StringLength(30)]
    [Unicode(false)]
    public string NotificationType { get; set; } = null!;

    /// <summary>
    /// Context-specific ID (e.g. AssetApprovalId) for deduplication and navigation.
    /// </summary>
    public int? ReferenceId { get; set; }

    [StringLength(200)]
    [Unicode(false)]
    public string? ReferenceUrl { get; set; }

    public bool IsRead { get; set; }

    public bool EmailSent { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreatedDate { get; set; }

    [ForeignKey("UserId")]
    public virtual TblUser User { get; set; } = null!;
}
