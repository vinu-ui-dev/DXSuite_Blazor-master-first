using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblDevicePairingRequest")]
[Index(nameof(PairingCodeHash), Name = "IX_tblDevicePairingRequest_CodeHash")]
[Index(nameof(Status), Name = "IX_tblDevicePairingRequest_Status")]
public class TblDevicePairingRequest
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

    [Column(TypeName = "datetime")]
    public DateTime ExpiresAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ResolvedAt { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Manufacturer { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Model { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? SerialNumber { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? IpAddress { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? MacAddress { get; set; }

    public int FailedAttempts { get; set; }

    [Column("ResolvedByUserId")]
    public int? ResolvedByUserId { get; set; }

    // The flavour the player declares when it asks to pair. Flavour is written to the device row
    // from the approved request rather than from a heartbeat, so a device cannot choose its own
    // update stream by declaring one.
    [StringLength(50)]
    [Unicode(false)]
    public string? Platform { get; set; }

    // Token delivery was an in-memory ConcurrentDictionary, so an approved player lost its token
    // to an app-pool recycle and to every node but the one that approved it. Protected at rest.
    public string? DeliveryTokenProtected { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DeliveryTokenCreatedAt { get; set; }

    // Set on the first successful poll. The status poll returned the plaintext token on every poll
    // for fifteen minutes, keyed only on the request GUID, without requiring the six-digit code.
    [Column(TypeName = "datetime")]
    public DateTime? DeliveryTokenCollectedAt { get; set; }

    [ForeignKey("ResolvedByUserId")]
    public virtual TblUser? ResolvedByUser { get; set; }
}
