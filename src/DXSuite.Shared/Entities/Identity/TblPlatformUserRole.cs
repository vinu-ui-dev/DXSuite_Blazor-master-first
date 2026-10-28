using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblPlatformUserRole")]
public class TblPlatformUserRole
{
    [Key]
    public Guid PlatformUserRoleId { get; set; }

    public Guid PlatformUserId { get; set; }

    public int RoleId { get; set; }

    public Guid? GrantedByPlatformUserId { get; set; }

    public DateTime GrantedDate { get; set; }

    [ForeignKey(nameof(PlatformUserId))]
    public TblPlatformUser PlatformUser { get; set; } = null!;

    [ForeignKey(nameof(RoleId))]
    public TblPlatformRole Role { get; set; } = null!;
}
