using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// The Pulse publisher's per-device bookkeeping: which Pulse device it last wrote to, heartbeat and
/// attempt history, and why a heartbeat was suppressed or the link marked broken. Unlink and change link
/// delete the row so a relinked device starts clean.
/// </summary>
[Table("SentinelPulseDeviceState")]
public partial class SentinelPulseDeviceState
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("PulseCustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string PulseCustomerId { get; set; } = null!;

    [Column("PulseDeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string PulseDeviceId { get; set; } = null!;

    [Column("LastHeartbeatSentUtc", TypeName = "datetime2")]
    public DateTime? LastHeartbeatSentUtc { get; set; }

    [Column("LastAttemptUtc", TypeName = "datetime2")]
    public DateTime? LastAttemptUtc { get; set; }

    [Column("LastSuccessUtc", TypeName = "datetime2")]
    public DateTime? LastSuccessUtc { get; set; }

    [Column("ConsecutiveFailures")]
    public int ConsecutiveFailures { get; set; }

    [Column("SuppressionReason")]
    [StringLength(30)]
    [Unicode(false)]
    public string? SuppressionReason { get; set; }

    [Column("LastError")]
    [StringLength(500)]
    public string? LastError { get; set; }

    /// <summary>Set when the linked Pulse row is gone; the publisher stops writing until Retry.</summary>
    [Column("LinkBrokenUtc", TypeName = "datetime2")]
    public DateTime? LinkBrokenUtc { get; set; }
}
