using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

[Table("tblCountryLanguage")]
public partial class TblCountryLanguage
{
    [Key]
    [Column("COUNTRY_CODE")]
    [StringLength(2)]
    [Unicode(false)]
    public string CountryCode { get; set; } = null!;

    [Column("LANGUAGE_CODE")]
    [StringLength(3)]
    [Unicode(false)]
    public string LanguageCode { get; set; } = null!;
}
