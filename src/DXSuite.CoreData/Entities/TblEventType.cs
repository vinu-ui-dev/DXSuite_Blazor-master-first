using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

[Table("tblEventType")]
public partial class TblEventType
{
    [Key]
    [Column("EventTypeID")]
    public int EventTypeId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string EventName { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [InverseProperty("EventType")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();
}
