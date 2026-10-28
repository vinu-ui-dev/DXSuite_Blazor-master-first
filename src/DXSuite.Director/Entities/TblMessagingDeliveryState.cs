using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

/// <summary>
/// The tenant's messaging delivery version and doorbell state (INK-09): one row per tenant database, in SQL so every
/// Director instance sees the same values (HOST-04 item 1). Every change that can alter the delivery body gives <see
/// cref="Version"/> a new value in its own transaction; the leased doorbell job alone writes the Nudged columns.
/// </summary>
[Table("tblMessagingDeliveryState")]
public partial class TblMessagingDeliveryState
{
    /// <summary>Always <see cref="SingleRowId"/>; the model seeds the row so a bump never has to create it.</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int MessagingDeliveryStateId { get; set; }

    public const int SingleRowId = 1;

    /// <summary>
    /// A fresh value per change rather than a counter: a bump writes it without reading the old one, so two changes
    /// committing at once never write the same value and neither is missed.
    /// </summary>
    public Guid Version { get; set; }

    /// <summary>The <see cref="Version"/> the doorbell last rang for; null until it first rings.</summary>
    public Guid? NudgedVersion { get; set; }

    /// <summary>The delivery body's ETag when the doorbell last rang, which its 5-second check compares against.</summary>
    [StringLength(64)]
    public string? NudgedETag { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? NudgedUtc { get; set; }
}
