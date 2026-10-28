using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

/// <summary>
/// The tenant's CoreAPI feed state as the leased watcher last saw it (CAPI-05): one row per tenant database, in
/// SQL so every Director instance reads the same state (HOST-04 item 1). Only the watcher writes it.
/// </summary>
/// <remarks>
/// One row holds everything the watcher follows for the tenant, so a second CoreAPI feed it has to watch adds columns
/// here rather than another table.
/// </remarks>
[Table("tblCoreApiFeedState")]
public partial class TblCoreApiFeedState
{
    /// <summary>Always <see cref="SingleRowId"/>, so two writers racing to create the row cannot make two.</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int CoreApiFeedStateId { get; set; }

    public const int SingleRowId = 1;

    /// <summary>CoreAPI's last <c>FeedStatus.Status</c>; <c>OK</c> means the flight feed is up (CAPI-04).</summary>
    [StringLength(100)]
    public string? Status { get; set; }

    /// <summary>False while no configured CoreAPI server answers.</summary>
    public bool Reachable { get; set; }

    /// <summary>CoreAPI's flight Outage mode switch as its hub last reported it; null until it has.</summary>
    public bool? SwitchOn { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime ChangedUtc { get; set; }
}
