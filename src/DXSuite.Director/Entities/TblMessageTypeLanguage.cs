using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One language of a Message Type (MSG-03). LanguageCode is a code from the product language catalogue
/// (ILanguageProvider) and matches tblMessageText.LanguageCode; SortOrder is the order the text boxes and showings take.
/// </summary>
[Table("tblMessageTypeLanguage")]
[PrimaryKey(nameof(MessageTypeId), nameof(LanguageCode))]
public partial class TblMessageTypeLanguage
{
    public int MessageTypeId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string LanguageCode { get; set; } = null!;

    public int SortOrder { get; set; }

    [ForeignKey("MessageTypeId")]
    [InverseProperty("Languages")]
    public virtual TblMessageType MessageType { get; set; } = null!;
}
