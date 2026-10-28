using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblDeviceModelConfig")]
public class TblDeviceModelConfig
{
    [Key]
    public int DeviceModelConfigId { get; set; }

    [Required]
    [StringLength(255)]
    public string Manufacturer { get; set; } = "";

    [Required]
    [StringLength(255)]
    public string Model { get; set; } = "";

    /// <summary>
    /// JSON array of EncodingProfile objects defining re-encode thresholds for this device make/model.
    /// </summary>
    public string? ProfilesJson { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
