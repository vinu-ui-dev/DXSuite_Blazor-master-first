using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelRemediationPolicy")]
public partial class RemediationPolicy
{
    [Key]
    [Column("PolicyID")]
    public int PolicyId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("PolicyName")]
    [StringLength(100)]
    [Unicode(false)]
    public string PolicyName { get; set; } = null!;

    [Column("PolicyJson")]
    [Unicode(false)]
    public string PolicyJson { get; set; } = null!;

    [Column("Scope")]
    [StringLength(100)]
    [Unicode(false)]
    public string Scope { get; set; } = null!;

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [Column("LastEditUser")]
    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }
}
