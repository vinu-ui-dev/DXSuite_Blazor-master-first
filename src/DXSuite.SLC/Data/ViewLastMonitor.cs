using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.SLC.Data;

/// <summary>
/// Read-only view returning the most recent monitor reading per device and monitor item.
/// Maps to the Pulse DB view View_LastMonitor.
/// </summary>
[Keyless]
public class ViewLastMonitor
{
    [StringLength(50)]
    public string DeviceId { get; set; } = string.Empty;

    [StringLength(150)]
    public string? DeviceDescription { get; set; }

    [Column("DeviceHeartbeat")]
    public DateTime? DeviceHeartbeat { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    public string? CustomerId { get; set; }

    public DateTime Heartbeat { get; set; }

    public DateTime? LocalHeartbeat { get; set; }

    [Column("MonitorItemID")]
    public int MonitorItemId { get; set; }

    [StringLength(50)]
    public string? Description { get; set; }

    [Column("ItemTypeID")]
    public int? ItemTypeId { get; set; }

    [Column("MonitorTypeID")]
    public int? MonitorTypeId { get; set; }

    [StringLength(50)]
    public string ItemType { get; set; } = string.Empty;

    [Unicode(false)]
    public string? ValueVarchar { get; set; }

    public byte[]? ValueVarbinary { get; set; }

    public int? ValueInteger { get; set; }

    public DateTime? ValueDateTime { get; set; }

    [StringLength(50)]
    public string? ErrorLevel { get; set; }

    [Unicode(false)]
    public string? ValueAll { get; set; }

    [Column("IPAddress")]
    [StringLength(20)]
    public string? Ipaddress { get; set; }
}
