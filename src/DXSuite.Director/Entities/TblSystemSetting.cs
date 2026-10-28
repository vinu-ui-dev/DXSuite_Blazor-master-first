using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Key-value store for runtime configuration (SMTP credentials, feature flags).
/// Stored in the database to avoid putting secrets in appsettings.json.
/// </summary>
[Table("tblSystemSetting")]
public partial class TblSystemSetting
{
    [Key]
    [StringLength(100)]
    [Unicode(false)]
    public string SettingKey { get; set; } = null!;

    [StringLength(2000)]
    public string? SettingValue { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }
}
