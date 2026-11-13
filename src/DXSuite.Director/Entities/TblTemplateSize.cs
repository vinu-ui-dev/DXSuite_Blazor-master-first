using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblTemplateSize", Schema = "aviation")]
public partial class TblTemplateSize
{
    [Key]
    [StringLength(64)]
    [Unicode(false)]
    public string SizeKey { get; set; } = null!;

    public int Width { get; set; }

    public int Height { get; set; }
}
