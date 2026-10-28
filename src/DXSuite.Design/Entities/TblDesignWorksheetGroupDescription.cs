using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Optional description row for a hardware Category Group header in a worksheet.
/// When IsEnabled, a NetSuite description item is pushed immediately before that group's
/// hardware line items so the PDF includes a formatted section header per category group.
/// </summary>
[Table("tblDesignWorksheetGroupDescription")]
public class TblDesignWorksheetGroupDescription
{
    [Key]
    public int Id { get; set; }

    public int WorksheetId { get; set; }

    public int CategoryGroupId { get; set; }

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

    [ForeignKey(nameof(CategoryGroupId))]
    public virtual TblDesignCategoryGroup? CategoryGroup { get; set; }
}
