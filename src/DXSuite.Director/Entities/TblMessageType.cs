using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessageType")]
[Index(nameof(Code), IsUnique = true, Name = "UX_tblMessageType_Code")]
public partial class TblMessageType
{
    [Key]
    public int MessageTypeId { get; set; }

    [Required]
    [StringLength(30)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary>Lower value wins when messages compete for the same screen region.</summary>
    public int Priority { get; set; }

    public MessageDisplayMode DisplayMode { get; set; }

    public const int MaxCharactersDefault = 160;
    public const int MaxCharactersLowerBound = 1;
    public const int MaxCharactersUpperBound = 500;

    /// <summary>Characters allowed in each language's text, counted per language (MSG-02).</summary>
    public int MaxCharacters { get; set; } = MaxCharactersDefault;

    /// <summary>
    /// Outranks every other type and every priority override (MSG-08). A filtered unique index allows one live type
    /// with the flag. EF does not order updates by a filtered index, so moving the flag saves the clear before the
    /// set, both in one transaction.
    /// </summary>
    public bool IsTopPriority { get; set; }

    public int DefaultDisplaySeconds { get; set; }

    public int DefaultHideSeconds { get; set; }

    [Required]
    [StringLength(9)]
    [Unicode(false)]
    public string DefaultTextColor { get; set; } = null!;

    [Required]
    [StringLength(9)]
    [Unicode(false)]
    public string DefaultBackgroundColor { get; set; } = null!;

    public bool IsDeleted { get; set; }

    [InverseProperty("MessageType")]
    public virtual ICollection<TblMessage> Messages { get; set; } = new List<TblMessage>();

    /// <summary>The languages every Message of this type is written in, in SortOrder (MSG-03).</summary>
    [InverseProperty("MessageType")]
    public virtual ICollection<TblMessageTypeLanguage> Languages { get; set; } = new List<TblMessageTypeLanguage>();

    [InverseProperty("MessageType")]
    public virtual ICollection<TblMessageTypeAssetLibrary> AssetLibraries { get; set; } = new List<TblMessageTypeAssetLibrary>();
}
