using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblParameter")]
[Index("KeyName", "CustomerId", Name = "IX_tblParameter_KeyName", IsUnique = true)]
public partial class TblParameter
{
    [Key]
    [Column("RecordID")]
    public int RecordId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string? CustomerId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string KeyName { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string Value { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string? Comment { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }
}
