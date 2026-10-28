using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Key/value pairs for device-specific metadata that becomes URL parameters for web content.
/// Common keys: zipCode, propertyName, locale
/// </summary>
[Table("tblDeviceMetadata")]
[Index("DeviceId", Name = "IX_DeviceMetadata_DeviceId")]
public partial class TblDeviceMetadata
{
    [Key]
    public int DeviceMetadataId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string MetadataKey { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string MetadataValue { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("DeviceId")]
    [InverseProperty("DeviceMetadata")]
    public virtual TblDevice? Device { get; set; }
}
