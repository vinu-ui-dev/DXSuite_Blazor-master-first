using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblControlClientPairingRequest")]
[Index(nameof(PairingCodeHash), Name = "IX_tblControlClientPairingRequest_CodeHash")]
[Index(nameof(Status), Name = "IX_tblControlClientPairingRequest_Status")]
public class TblControlClientPairingRequest
{
    [Key]
    [Column("PairingRequestId")]
    public Guid PairingRequestId { get; set; }

    [Required]
    [StringLength(128)]
    [Unicode(false)]
    public string PairingCodeHash { get; set; } = null!;

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = "pending";

    [Column("ControlClientId")]
    public int? ControlClientId { get; set; }

    [StringLength(45)]
    [Unicode(false)]
    public string? ClientIp { get; set; }

    [StringLength(100)]
    public string? ClientHostname { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ExpiresAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ResolvedAt { get; set; }

    public int FailedAttempts { get; set; }

    [Column("ResolvedByUserId")]
    public int? ResolvedByUserId { get; set; }

    [ForeignKey("ControlClientId")]
    public virtual TblControlClient? ControlClient { get; set; }

    [ForeignKey("ResolvedByUserId")]
    public virtual TblUser? ResolvedByUser { get; set; }
}
