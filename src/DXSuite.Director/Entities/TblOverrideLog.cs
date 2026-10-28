using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblOverrideLog")]
[Index(nameof(OverrideId), Name = "IX_tblOverrideLog_OverrideId")]
[Index(nameof(Timestamp), Name = "IX_tblOverrideLog_Timestamp", IsDescending = new[] { true })]
public partial class TblOverrideLog
{
    [Key]
    public int LogId { get; set; }

    public int OverrideId { get; set; }

    [StringLength(100)]
    public string? OverrideName { get; set; }

    [Required]
    [StringLength(50)]
    public string Action { get; set; } = null!;

    [Required]
    [StringLength(50)]
    public string TriggerSource { get; set; } = null!;

    [StringLength(100)]
    public string? TriggerUser { get; set; }

    [StringLength(50)]
    public string? TriggerIp { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Timestamp { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
