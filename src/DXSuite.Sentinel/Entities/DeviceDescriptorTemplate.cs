using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelDeviceDescriptorTemplate")]
public partial class DeviceDescriptorTemplate
{
    [Key]
    [Column("TemplateID")]
    public int TemplateId { get; set; }

    [Column("TemplateName")]
    [StringLength(50)]
    [Unicode(false)]
    public string TemplateName { get; set; } = null!;

    [Column("DisplayName")]
    [StringLength(100)]
    [Unicode(false)]
    public string? DisplayName { get; set; }

    [Column("Description")]
    [StringLength(500)]
    [Unicode(false)]
    public string? Description { get; set; }

    [Column("TemplateJson")]
    [Unicode(false)]
    public string TemplateJson { get; set; } = null!;

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
