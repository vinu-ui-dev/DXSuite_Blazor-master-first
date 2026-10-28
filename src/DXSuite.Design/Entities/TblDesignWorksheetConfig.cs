using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// A saved worksheet configuration snapshot scoped to a customer.
/// Stores the full worksheet state as JSON so it can be loaded onto any worksheet of the same customer,
/// with catalog-item cross-referencing at load time to handle items that were removed since saving.
/// </summary>
[Table("tblDesignWorksheetConfig")]
public class TblDesignWorksheetConfig
{
    [Key]
    public int ConfigId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    /// <summary>Full worksheet snapshot serialized as JSON (see WorksheetConfigDto schema).</summary>
    public string ConfigJson { get; set; } = "{}";
}
