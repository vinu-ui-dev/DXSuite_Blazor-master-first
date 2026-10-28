using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

[Table("tblLlmPromptConfig")]
public partial class TblLlmPromptConfiguration
{
    [Key]
    [Column("PromptId")]
    public int PromptId { get; set; }

    [Column("UseCaseId")]
    [StringLength(50)]
    public string UseCaseId { get; set; } = null!;

    [Column("DisplayName")]
    [StringLength(100)]
    public string DisplayName { get; set; } = null!;

    [Column("ProviderId")]
    [StringLength(50)]
    public string? ProviderId { get; set; }

    [Column("SystemPrompt")]
    public string SystemPrompt { get; set; } = null!;

    [Column("Temperature")]
    public float Temperature { get; set; } = 0.1f;

    [Column("MaxTokens")]
    public int MaxTokens { get; set; } = 1000;

    [Column("ResponseFormat")]
    [StringLength(20)]
    public string? ResponseFormat { get; set; }

    [Column("SupportsText")]
    public bool SupportsText { get; set; }

    [Column("SupportsVision")]
    public bool SupportsVision { get; set; }

    [Column("LastTestedDate")]
    public DateTime? LastTestedDate { get; set; }

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; } = true;

    [Column("CreateDate")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate")]
    public DateTime? LastEditDate { get; set; }

    [Column("LastEditUser")]
    [StringLength(100)]
    public string? LastEditUser { get; set; }
}
