using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessageType")]
[Index(nameof(Code), IsUnique = true, Name = "UX_tblMessageType_Code")]
public partial class TblMessageType
{
    [Key]
    public int MessageTypeId { get; set; }

    [Required]
    [StringLength(30)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary>Lower value wins when messages compete for the same screen region.</summary>
    public int Priority { get; set; }

    public MessageDisplayMode DisplayMode { get; set; }

    public bool AllowsSignVideo { get; set; }

    public int DefaultDisplaySeconds { get; set; }

    public int DefaultHideSeconds { get; set; }

    [InverseProperty("MessageType")]
    public virtual ICollection<TblMessage> Messages { get; set; } = new List<TblMessage>();
}
