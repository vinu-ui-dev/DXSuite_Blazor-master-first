using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblLocalAccount")]
[Index(nameof(NormalizedEmail), IsUnique = true, Name = "IX_LocalAccount_Email")]
public class TblLocalAccount
{
    [Key]
    public int LocalAccountId { get; set; }

    [Required]
    [StringLength(256)]
    public string Email { get; set; } = null!;

    [Required]
    [StringLength(256)]
    public string NormalizedEmail { get; set; } = null!;

    [Required]
    [StringLength(512)]
    public string PasswordHash { get; set; } = null!;

    public bool EmailConfirmed { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    public int UserId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastLoginDate { get; set; }

    [StringLength(128)]
    public string? ConfirmationToken { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ConfirmationTokenExpiry { get; set; }

    [ForeignKey("UserId")]
    public virtual TblUser User { get; set; } = null!;
}
