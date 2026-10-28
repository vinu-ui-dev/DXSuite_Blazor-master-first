using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblControlClientToken")]
[Index(nameof(TokenHash), IsUnique = true, Name = "IX_tblControlClientToken_TokenHash")]
public class TblControlClientToken
{
    [Key]
    [Column("ControlClientTokenId")]
    public int ControlClientTokenId { get; set; }

    [Column("ControlClientId")]
    public int ControlClientId { get; set; }

    [Required]
    [StringLength(128)]
    [Unicode(false)]
    public string TokenHash { get; set; } = null!;

    [StringLength(16)]
    [Unicode(false)]
    public string? TokenPrefix { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime? LastUsedAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("ControlClientId")]
    [InverseProperty("ControlClientTokens")]
    public virtual TblControlClient ControlClient { get; set; } = null!;
}
