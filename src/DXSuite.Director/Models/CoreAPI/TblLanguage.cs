using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Models.CoreAPI;

[Table("tblLanguage")]
public partial class TblLanguage
{
    [Key]
    [StringLength(10)]
    [Unicode(false)]
    public string LanguageCode { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string LanguageName { get; set; } = null!;
}
