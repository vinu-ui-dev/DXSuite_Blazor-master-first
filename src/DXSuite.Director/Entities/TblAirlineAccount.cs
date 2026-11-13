using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAirlineAccount", Schema = "aviation")]
public partial class TblAirlineAccount
{
    [Key]
    public int AirlineId { get; set; }

    [StringLength(200)]
    public string? NameEnglish { get; set; }

    [StringLength(200)]
    public string? NameFrench { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string? IataCode { get; set; }

    [StringLength(4)]
    [Unicode(false)]
    public string? IcaoCode { get; set; }

    [StringLength(200)]
    public string? CuppsName { get; set; }

    public bool IsCanada { get; set; }

    public bool IsUnitedStates { get; set; }

    public bool IsInternational { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string? StandardBackgroundColour { get; set; }

    public byte? StandardTextColour { get; set; }

    public int? DcsProviderId { get; set; }

    public int? AllianceId { get; set; }

    public bool ShowAirportStandardTemplates { get; set; }

    public int? DefaultCounterTemplateId { get; set; }

    public byte? PickerTab { get; set; }

    public int? UserGroupId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? RetiredUtc { get; set; }

    public int? FullTemplateLibraryId { get; set; }

    public int? HalfTemplateLibraryId { get; set; }
}
