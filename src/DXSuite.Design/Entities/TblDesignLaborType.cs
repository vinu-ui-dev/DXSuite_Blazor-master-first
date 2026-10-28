using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Determines which staffing scenario a labor type participates in.
/// Internal: always billed at internal rates.
/// LeadSub: internal in All Us + Our Leads; sub rates in All Subs.
/// Sub: internal only in All Us; sub rates in Our Leads + All Subs.
/// </summary>
public enum SubRole
{
    Internal,
    LeadSub,
    Sub,
}

[Table("tblDesignLaborType")]
public class TblDesignLaborType
{
    [Key]
    public int LaborTypeId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>NetSuite inventory item ID pushed as a line item when the worksheet is sent to NetSuite.</summary>
    [MaxLength(100)]
    public string? NetsuiteItemId { get; set; }

    /// <summary>Snapshot of the linked NetSuite item name — avoids a live lookup on render.</summary>
    [MaxLength(300)]
    public string? NetsuiteItemName { get; set; }

    public decimal InternalCostPerHr { get; set; }
    public decimal InternalPricePerHr { get; set; }

    /// <summary>Stored as string via HasConversion — see DesignDbContext.OnModelCreating.</summary>
    public SubRole SubRole { get; set; } = SubRole.Internal;

    /// <summary>
    /// When true, this labor type's hours are derived from a percentage of all column-based hours
    /// rather than being assigned directly to columns. Use for overhead roles (e.g. PM at 15%).
    /// </summary>
    public bool IsPercentageOfColumns { get; set; }

    /// <summary>Percentage of total column-based hours; used when IsPercentageOfColumns is true.</summary>
    public decimal? ColumnPercentage { get; set; }

    /// <summary>
    /// When set and at least one staff member is assigned to this labor type, the worksheet labor
    /// list will show at least this many hours regardless of what the column calculation produces.
    /// Null = no minimum enforced.
    /// </summary>
    public decimal? MinimumHours { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, personnel in this role physically travel to the job site.
    /// Combined with SubRole, this determines which staffing scenarios include travel costs:
    ///   All Us    — all IsTravelingRole types count.
    ///   Our Leads — IsTravelingRole types where SubRole != Sub count (leads stay; subs are local).
    ///   Subs      — only IsTravelingRole + Internal types count (purely internal roles still travel).
    /// </summary>
    public bool IsTravelingRole { get; set; } = true;

    /// <summary>Permanent labor types always shown in the worksheet, regardless of item presence.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Controls display order in the equipment table labor columns (lowest = leftmost).</summary>
    public int SortOrder { get; set; }

    // Navigation
    public virtual ICollection<TblDesignLaborTypeColumn> ColumnAssignments { get; set; } = [];
}
