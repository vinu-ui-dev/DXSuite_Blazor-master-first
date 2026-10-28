using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("TblContentChannel")]
public class TblContentChannel
{
    [Key]
    public int ChannelId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    [Column("LocationID")]
    public int? LocationId { get; set; }

    [Column("DisplayID")]
    public int? DisplayId { get; set; }

    // 'engage' or 'ink-folder'
    [Required]
    [StringLength(50)]
    public string SourceType { get; set; } = "";

    // Engage channels only — one-to-one with the backing TblDevice.
    // varchar(50) to match tblDevice.DeviceID which is Unicode(false).
    [StringLength(50)]
    [Unicode(false)]
    public string? DeviceId { get; set; }

    // Ink-folder channels only
    public int? ConnectorId { get; set; }

    [StringLength(200)]
    public string? ExternalId { get; set; }

    [StringLength(200)]
    public string? ExternalName { get; set; }

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    // Persisted sync state — stores the filename→Ink file ID map as JSON across sync cycles
    public string? ExtendedData { get; set; }

    // Navigation
    [ForeignKey("LocationId")]
    public virtual TblLocation? Location { get; set; }

    [ForeignKey("DisplayId")]
    public virtual TblDisplay? Display { get; set; }

    [ForeignKey("DeviceId")]
    public virtual TblDevice? Device { get; set; }

    [InverseProperty("Channel")]
    public virtual ICollection<TblContentChannelMetadata> Metadata { get; set; } = [];
}
