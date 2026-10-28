using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblContentPackageEnvironmentLabel")]
public class TblContentPackageEnvironmentLabel
{
    [Key]
    public int ContentPackageEnvironmentLabelId { get; set; }

    [Required]
    [StringLength(100)]
    public string Label { get; set; } = null!;

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    [StringLength(255)]
    public string? CreatedByUser { get; set; }
}
