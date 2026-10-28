using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblDeviceToken")]
[Index(nameof(TokenHash), IsUnique = true, Name = "IX_tblDeviceToken_TokenHash")]
public class TblDeviceToken
{
    [Key]
    [Column("DeviceTokenId")]
    public int DeviceTokenId { get; set; }

    [Column("DeviceId")]
    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    [Required]
    [StringLength(128)]
    [Unicode(false)]
    public string TokenHash { get; set; } = null!;

    [StringLength(16)]
    [Unicode(false)]
    public string? TokenPrefix { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime? LastUsedAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    public Guid? PairingRequestId { get; set; }

    [Column("ApprovedByUserId")]
    public int? ApprovedByUserId { get; set; }

    [ForeignKey("DeviceId")]
    public virtual TblDevice? Device { get; set; }

    [ForeignKey("ApprovedByUserId")]
    public virtual TblUser? ApprovedByUser { get; set; }
}
