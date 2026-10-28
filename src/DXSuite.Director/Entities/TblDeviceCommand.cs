using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Durable server-to-player command queue. Commands are drained by the device's next
/// PlayerContent poll and survive app restarts, unlike the in-memory queues they replaced.
/// A device has at most one Queued command per type: re-queuing supersedes the previous one.
/// </summary>
[Table("tblDeviceCommand")]
[Index(nameof(DeviceId), nameof(Status), Name = "IX_tblDeviceCommand_Device_Status")]
[Index(nameof(RequestId), Name = "IX_tblDeviceCommand_RequestId")]
public class TblDeviceCommand
{
    [Key]
    public int CommandId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    /// <summary>"cec", "screenshot", or "flushCache"</summary>
    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string CommandType { get; set; } = null!;

    /// <summary>Queued, Delivered, Acknowledged, Expired, or Superseded</summary>
    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = "Queued";

    /// <summary>Command-specific parameters as JSON (e.g. CEC action/output). Null for flushCache.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Screenshot upload credential: the device uploads its capture against this id on the
    /// anonymous SOS endpoints, so it gets its own indexed column instead of living in PayloadJson.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string? RequestId { get; set; }

    [StringLength(255)]
    public string? IssuedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime IssuedAt { get; set; }

    /// <summary>Commands not delivered by this time expire instead of executing stale.</summary>
    [Column(TypeName = "datetime")]
    public DateTime ExpiresAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DeliveredAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? AcknowledgedAt { get; set; }
}
