using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// Per-customer ITSM connection configuration. Stores the credentials and server details
/// needed to communicate with the customer's ITSM platform (Jira SM, ServiceNow, etc.).
/// Replaces the Pulse TblCustomerIntegration dependency for Sentinel ITSM operations.
/// </summary>
[Table("SentinelItsmConnection")]
public partial class ItsmConnection
{
    [Key]
    [Column("ConnectionID")]
    public int ConnectionId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    /// <summary>
    /// Adapter type identifier matching IItsmAdapter.PlatformId (e.g., "JiraSM", "ServiceNow", "rest-generic")
    /// </summary>
    [Column("AdapterType")]
    [StringLength(50)]
    [Unicode(false)]
    public string AdapterType { get; set; } = null!;

    /// <summary>
    /// ITSM platform base URL (e.g., "https://mycompany.atlassian.net")
    /// </summary>
    [Column("Server")]
    [StringLength(500)]
    [Unicode(false)]
    public string? Server { get; set; }

    [Column("Port")]
    public int? Port { get; set; }

    /// <summary>
    /// Authentication username or email
    /// </summary>
    [Column("UserName")]
    [StringLength(100)]
    [Unicode(false)]
    public string? UserName { get; set; }

    /// <summary>
    /// Authentication password, project key, or secondary credential depending on adapter type
    /// </summary>
    [Column("Password")]
    [StringLength(500)]
    [Unicode(false)]
    public string? Password { get; set; }

    /// <summary>
    /// API token, bearer token, or JSON config blob depending on adapter type
    /// </summary>
    [Column("Token")]
    [Unicode(false)]
    public string? Token { get; set; }

    /// <summary>
    /// Failover priority when multiple connections exist for the same customer (lower = higher priority)
    /// </summary>
    [Column("Priority")]
    public int Priority { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Extra adapter-specific settings as JSON (OAuth scopes, CMDB mappings, ticket templates, etc.)
    /// </summary>
    [Column("ConfigJson")]
    [Unicode(false)]
    public string? ConfigJson { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }
}
