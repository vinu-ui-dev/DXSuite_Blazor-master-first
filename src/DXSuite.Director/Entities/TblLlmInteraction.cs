using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblLlmInteraction")]
public partial class TblLlmInteraction
{
    [Key]
    [Column("InteractionID")]
    public long InteractionId { get; set; }

    [Column("Provider")]
    [StringLength(50)]
    [Unicode(false)]
    public string Provider { get; set; } = null!;

    [Column("Model")]
    [StringLength(100)]
    [Unicode(false)]
    public string Model { get; set; } = null!;

    [Column("PromptHash")]
    [StringLength(64)]
    [Unicode(false)]
    public string PromptHash { get; set; } = null!;

    [Column("PromptTokens")]
    public int PromptTokens { get; set; }

    [Column("CompletionTokens")]
    public int CompletionTokens { get; set; }

    [Column("RequestJson")]
    [Unicode(false)]
    public string? RequestJson { get; set; }

    [Column("ResponseJson")]
    [Unicode(false)]
    public string? ResponseJson { get; set; }

    [Column("LatencyMs")]
    public long LatencyMs { get; set; }

    [Column("Success")]
    public bool Success { get; set; }

    [Column("ErrorMessage")]
    [StringLength(500)]
    [Unicode(false)]
    public string? ErrorMessage { get; set; }

    [Column("Purpose")]
    [StringLength(50)]
    [Unicode(false)]
    public string Purpose { get; set; } = null!;

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
