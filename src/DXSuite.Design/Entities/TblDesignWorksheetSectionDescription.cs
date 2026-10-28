using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Optional description row for a non-hardware worksheet section (Labor, Misc, or Travel).
/// When IsEnabled, a NetSuite description item is pushed to the estimate immediately before
/// that section's line items so the PDF includes a formatted header for the section.
/// </summary>
[Table("tblDesignWorksheetSectionDescription")]
public class TblDesignWorksheetSectionDescription
{
    [Key]
    public int Id { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Section this row belongs to: "Labor", "Misc", or "Travel".</summary>
    [Required, MaxLength(20)]
    public string Section { get; set; } = string.Empty;

    /// <summary>When true, the description row is included in the NetSuite estimate push.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>NetSuite internal ID of the descriptionitem record to use as the line item.</summary>
    [MaxLength(100)]
    public string? DescriptionItemId { get; set; }

    /// <summary>Snapshot of the NetSuite description item's display name.</summary>
    [MaxLength(300)]
    public string? DescriptionItemName { get; set; }

    /// <summary>Freeform text placed in the NetSuite line's description field. Newlines are preserved.</summary>
    [MaxLength(4000)]
    public string? Description { get; set; }

    /// <summary>NetSuite location internal ID — required by NS on description lines.</summary>
    [MaxLength(100)]
    public string? LocationId { get; set; }

    /// <summary>Snapshot of the NetSuite location name.</summary>
    [MaxLength(300)]
    public string? LocationName { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }
}
