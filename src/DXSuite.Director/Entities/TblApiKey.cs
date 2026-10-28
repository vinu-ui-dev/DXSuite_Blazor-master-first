using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblApiKey")]
[Index(nameof(ApiKeyHash), IsUnique = true, Name = "IX_tblApiKey_ApiKeyHash")]
public partial class TblApiKey
{
    [Key]
    [Column("ApiKeyId")]
    public int ApiKeyId { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string? ApiKey { get; set; }

    [StringLength(128)]
    [Unicode(false)]
    public string? ApiKeyHash { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string? KeyPrefix { get; set; }

    [Required]
    [StringLength(100)]
    [Unicode(false)]
    public string KeyName { get; set; } = null!;

    [Column("LocationId")]
    public int? LocationId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Permissions { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime? ExpiresAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastUsedAt { get; set; }

    public long CallCount { get; set; }

    [Column("CreatedBy")]
    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("TblApiKeys")]
    public virtual TblLocation? Location { get; set; }

    [ForeignKey("CreatedBy")]
    public virtual TblUser? CreatedByUser { get; set; }
}
