using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Catalog of NetSuite-linked items used in the Miscellaneous and Travel worksheet sections.
/// IsTravel = false → appears in the Misc subtab and can be added to the Misc worksheet section.
/// IsTravel = true  → appears in the Travel subtab and can be added to the Travel worksheet section.
/// Travel items are excluded from the "All Subs" scenario total because sub contractors don't incur travel costs.
/// </summary>
[Table("tblDesignMiscItem")]
public class TblDesignMiscItem
{
    [Key]
    public int MiscItemId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>False = Miscellaneous catalog; true = Travel catalog.</summary>
    public bool IsTravel { get; set; }

    /// <summary>NetSuite internal item ID — required; used when pushing to a NetSuite estimate.</summary>
    [Required, MaxLength(100)]
    public string NetsuiteItemId { get; set; } = string.Empty;

    /// <summary>Snapshot of the NetSuite item display name or item ID.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public decimal DefaultCost { get; set; }
    public decimal DefaultSellPrice { get; set; }

    /// <summary>
    /// Travel items only. When true, cost scales with the number of traveling personnel in each
    /// scenario (e.g. Airfare, Hotel, Per Diem). When false, the cost is flat — billed once as
    /// long as at least one person travels (e.g. a single rental car for the whole crew).
    /// Ignored for non-travel items (IsTravel = false).
    /// </summary>
    public bool IsPerPerson { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
