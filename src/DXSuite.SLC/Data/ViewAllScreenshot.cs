using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.SLC.Data;

/// <summary>
/// Read-only view returning all screenshot monitor readings across devices.
/// Maps to the Pulse DB view View_AllScreenshots.
/// Screenshot records are identified by MonitorItemId == 10.
/// </summary>
[Keyless]
public class ViewAllScreenshot
{
    [Column("DeviceID")]
    [StringLength(50)]
    public string DeviceId { get; set; } = string.Empty;

    public DateTime Heartbeat { get; set; }

    [Column("MonitorItemID")]
    public int MonitorItemId { get; set; }

    [Unicode(false)]
    public string? ValueVarchar { get; set; }

    public byte[]? ValueVarbinary { get; set; }
}
