using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblEventSeverity")]
public partial class TblEventSeverity
{
    [Key]
    public int EventSeverityId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? SeverityName { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [InverseProperty("EventSeverity")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();
}
