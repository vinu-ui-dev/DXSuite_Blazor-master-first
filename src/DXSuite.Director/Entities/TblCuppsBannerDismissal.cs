using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCuppsBannerDismissal")]
[PrimaryKey(nameof(MessageActivationId), nameof(SessionId))]
public partial class TblCuppsBannerDismissal
{
    public int MessageActivationId { get; set; }

    public string SessionId { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime? DismissedUtc { get; set; }
}
