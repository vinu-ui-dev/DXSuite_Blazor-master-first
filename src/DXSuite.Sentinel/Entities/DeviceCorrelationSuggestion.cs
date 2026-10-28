using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// A proposed (but unconfirmed) association between two device records that look like the same physical
/// device by a weak signal (name/IP/model). Strong-key matches (exact MAC/serial) auto-link without a
/// suggestion; weak matches surface here for a human to confirm-merge or dismiss in the Devices UI.
/// </summary>
[Table("SentinelDeviceCorrelationSuggestion")]
public partial class DeviceCorrelationSuggestion
{
    [Key]
    [Column("SuggestionID")]
    public int SuggestionId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("PrimaryDeviceInstanceID")]
    public int PrimaryDeviceInstanceId { get; set; }

    [Column("CandidateDeviceInstanceID")]
    public int CandidateDeviceInstanceId { get; set; }

    [Column("Reason")]
    [StringLength(200)]
    [Unicode(false)]
    public string Reason { get; set; } = null!;

    /// <summary>pending | confirmed | dismissed.</summary>
    [Column("Status")]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = "pending";

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }
}
