using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// The tenant's connection to CoreAPI (CAPI-01): one row per tenant database. Kept in SQL rather than in
/// memory so every Director instance uses the same key and starts from the same last good server (HOST-04).
/// </summary>
[Table("tblCoreApiConnection")]
public partial class TblCoreApiConnection
{
    [Key]
    public int CoreApiConnectionId { get; set; }

    /// <summary>The Director key, protected with Data Protection by <c>CoreApiKeyProtector</c>.</summary>
    [Required]
    public string DirectorKeyProtected { get; set; } = null!;

    /// <summary>The server that last answered, from any instance; null until one has.</summary>
    [StringLength(400)]
    public string? LastGoodServerUrl { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? LastGoodUtc { get; set; }

    /// <summary>CoreAPI's SignalR hub address (C-08); null when the tenant has none configured.</summary>
    [StringLength(400)]
    public string? HubUrl { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime UpdatedUtc { get; set; }

    [InverseProperty(nameof(TblCoreApiServer.Connection))]
    public virtual ICollection<TblCoreApiServer> Servers { get; set; } = new List<TblCoreApiServer>();
}

/// <summary>One CoreAPI server address; <see cref="SortOrder"/> is the order the client tries them in.</summary>
[Table("tblCoreApiServer")]
[Index(nameof(CoreApiConnectionId), nameof(SortOrder), IsUnique = true, Name = "UX_tblCoreApiServer_Connection_Order")]
public partial class TblCoreApiServer
{
    [Key]
    public int CoreApiServerId { get; set; }

    public int CoreApiConnectionId { get; set; }

    [Required]
    [StringLength(400)]
    public string Url { get; set; } = null!;

    public int SortOrder { get; set; }

    [ForeignKey(nameof(CoreApiConnectionId))]
    [InverseProperty(nameof(TblCoreApiConnection.Servers))]
    public virtual TblCoreApiConnection Connection { get; set; } = null!;
}
