using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblMetadataDefinition")]
public partial class TblMetadataDefinition
{
    [Key]
    public int MetadataDefinitionId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string MetadataKey { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string DisplayName { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string FieldType { get; set; } = "string";

    public string? OptionsJson { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }
}
