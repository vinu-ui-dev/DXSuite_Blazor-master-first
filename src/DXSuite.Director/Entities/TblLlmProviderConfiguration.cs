using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblLlmProviderConfig")]
public partial class TblLlmProviderConfiguration
{
    [Key]
    [Column("ConfigID")]
    public int ConfigId { get; set; }

    [Column("ProviderId")]
    [StringLength(50)]
    public string ProviderId { get; set; } = null!;

    [Column("ProviderType")]
    [StringLength(30)]
    public string ProviderType { get; set; } = null!;

    [Column("DisplayName")]
    [StringLength(100)]
    public string DisplayName { get; set; } = null!;

    [Column("BaseUrl")]
    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [Column("ApiKey")]
    [StringLength(500)]
    public string? ApiKey { get; set; }

    [Column("Model")]
    [StringLength(100)]
    public string Model { get; set; } = null!;

    [Column("MaxTokens")]
    public int MaxTokens { get; set; } = 2000;

    [Column("DefaultTemperature")]
    public float DefaultTemperature { get; set; } = 0.1f;

    [Column("TimeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [Column("IsDefault")]
    public bool IsDefault { get; set; }

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; } = true;

    [Column("CredentialExpiresAt", TypeName = "datetime")]
    public DateTime? CredentialExpiresAt { get; set; }

    [Column("CreateDate")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate")]
    public DateTime? LastEditDate { get; set; }
}
