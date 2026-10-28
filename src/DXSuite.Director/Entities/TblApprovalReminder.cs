using System.ComponentModel.DataAnnotations.Schema;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// When an approver was last reminded of one waiting request, so each instance's reminder digest job (HOST-04) sends
/// the next reminder only once the chain's interval has passed, whichever instance sent the last one.
/// </summary>
[Table("tblApprovalReminder")]
[PrimaryKey(nameof(ApprovalRequestId), nameof(UserId))]
public partial class TblApprovalReminder
{
    public int ApprovalRequestId { get; set; }

    public int UserId { get; set; }

    public DateTime LastSentUtc { get; set; }

    [ForeignKey("ApprovalRequestId")]
    public virtual TblApprovalRequest ApprovalRequest { get; set; } = null!;

    [ForeignKey("UserId")]
    public virtual TblUser User { get; set; } = null!;
}
