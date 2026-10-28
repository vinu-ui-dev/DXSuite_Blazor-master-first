using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

// Key/value metadata for ink-folder content channels.
// Engage channels continue to use TblDeviceMetadata (keyed by DeviceId) so that
// the SignageController manifest pipeline remains unchanged.
[Table("TblContentChannelMetadata")]
public class TblContentChannelMetadata
{
    [Key]
    public int MetadataId { get; set; }

    public int ChannelId { get; set; }

    [Required]
    [StringLength(50)]
    public string MetadataKey { get; set; } = "";

    [StringLength(255)]
    public string? MetadataValue { get; set; }

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    [ForeignKey("ChannelId")]
    public virtual TblContentChannel Channel { get; set; } = null!;
}
