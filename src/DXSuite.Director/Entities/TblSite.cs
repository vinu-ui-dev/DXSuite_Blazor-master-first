using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// A physical place devices sit: a service center, a terminal, a branch. Assignment is manual on
/// every deployment and permanently so, because a device will one day report its own time zone but
/// no device will ever report its own street address.
///
/// A site deliberately carries no time zone. tblDevice.TimeZone is the only home for one, and
/// nothing derives a zone from the address: a postal code does not determine a zone, and Arizona is
/// exactly where such a lookup would be wrong.
/// </summary>
[Table("tblSite")]
public partial class TblSite
{
    [Key]
    public int SiteId { get; set; }

    /// <summary>Tenant scope, matching tblDevice.CustomerId.</summary>
    [StringLength(3)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    /// <summary>Not unique per customer: two sites may legitimately carry the same name.</summary>
    [StringLength(100)]
    public string SiteName { get; set; } = null!;

    [StringLength(255)]
    public string? Address1 { get; set; }

    [StringLength(255)]
    public string? Address2 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? StateProvince { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? PostalCode { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [InverseProperty("Site")]
    public virtual ICollection<TblDevice> Devices { get; set; } = new List<TblDevice>();
}
