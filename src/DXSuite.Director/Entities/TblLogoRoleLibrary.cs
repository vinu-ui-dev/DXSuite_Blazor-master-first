using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblLogoRoleLibrary", Schema = "aviation")]
[PrimaryKey(nameof(RoleKey), nameof(ConstraintId))]
public partial class TblLogoRoleLibrary
{
    public string RoleKey { get; set; } = null!;

    public int ConstraintId { get; set; }
}
