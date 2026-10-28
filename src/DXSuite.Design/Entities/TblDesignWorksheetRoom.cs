using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignWorksheetRoom")]
public class TblDesignWorksheetRoom
{
    [Key]
    public int RoomId { get; set; }

    public int WorksheetId { get; set; }

    [Required, MaxLength(200)]
    public string RoomName { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    public virtual ICollection<TblDesignWorksheetLineItem> LineItems { get; set; } = [];
}
