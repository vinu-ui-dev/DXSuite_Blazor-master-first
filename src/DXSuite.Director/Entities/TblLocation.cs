using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblLocation")]
public partial class TblLocation
{
    [Key]
    public int LocationId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    [StringLength(50)]
    [Unicode(false)]
    public string? LocationName { get; set; }

    public int LocationTypeId { get; set; }

    public int LocationGroupId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? LocationImagePath { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? BackgroundImagePath { get; set; }

    public double? DisplayAnchorTop { get; set; }

    public double? DisplayAnchorLeft { get; set; }

    public double? DisplayOffset { get; set; }

    public double? BackgroundImageHeight { get; set; }

    public double? BackgroundImageWidth { get; set; }

    public bool EnableShuffle { get; set; }

    public bool EnableFade { get; set; } = true;

    /// <summary>
    /// When true, enables hotel user permission filtering and content approval workflow for playlist locations
    /// </summary>
    public bool EnableContentApproval { get; set; } = false;

    /// <summary>
    /// When true, devices at this location receive only the content its metadata rules target at
    /// them. Scope applies whether or not this is set.
    /// </summary>
    public bool TargetContentByMetadata { get; set; }

    /// <summary>
    /// How the per-key verdicts of this location's metadata rules combine: <c>all</c> or <c>any</c>.
    /// </summary>
    [StringLength(10)]
    [Unicode(false)]
    public string MetadataMatchMode { get; set; } = "all";

    /// <summary>
    /// JSON column for location-specific extended properties (e.g., floor plans)
    /// </summary>
    public string? ExtendedData { get; set; }

    /// <summary>
    /// When true, this location is a transient cluster child of a VideoWall, auto-managed
    /// by the cluster lifecycle service. Airline users access it; admins access the parent.
    /// </summary>
    public bool IsTransientCluster { get; set; } = false;

    /// <summary>FK to the VideoWall location this cluster belongs to.</summary>
    public int? ParentVideoWallLocationId { get; set; }

    /// <summary>
    /// Identifies which counter run this transient location represents,
    /// e.g. "row-a:0-4" (GroupName:startIndex-endIndex).
    /// </summary>
    [StringLength(100)]
    [Unicode(false)]
    public string? ClusterKey { get; set; }

    /// <summary>FK to the airline UserGroup that holds this cluster.</summary>
    public int? OwningUserGroupId { get; set; }

    [ForeignKey("ParentVideoWallLocationId")]
    public virtual TblLocation? ParentVideoWallLocation { get; set; }

    [ForeignKey("OwningUserGroupId")]
    public virtual TblUserGroup? OwningUserGroup { get; set; }

    [ForeignKey("LocationGroupId")]
    [InverseProperty("TblLocations")]
    public virtual TblLocationGroup LocationGroup { get; set; } = null!;

    [ForeignKey("LocationTypeId")]
    [InverseProperty("TblLocations")]
    public virtual TblLocationType LocationType { get; set; } = null!;

    [InverseProperty("Location")]
    public virtual ICollection<TblAirlineLocation> TblAirlineLocations { get; set; } = new List<TblAirlineLocation>();

    [InverseProperty("Location")]
    public virtual ICollection<TblAsset> TblAssets { get; set; } = new List<TblAsset>();

    [InverseProperty("Location")]
    public virtual ICollection<TblDevice> TblDevices { get; set; } = new List<TblDevice>();

    [InverseProperty("Location")]
    public virtual ICollection<TblDisplay> TblDisplays { get; set; } = new List<TblDisplay>();

    [InverseProperty("Location")]
    public virtual ICollection<TblUserLocation> TblUserLocations { get; set; } = new List<TblUserLocation>();

    [InverseProperty("Location")]
    public virtual ICollection<TblUpdateDeployment> UpdateDeployments { get; set; } = new List<TblUpdateDeployment>();

    [InverseProperty("Location")]
    public virtual ICollection<TblApiKey> TblApiKeys { get; set; } = new List<TblApiKey>();

    [InverseProperty("Location")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();

    [InverseProperty("Location")]
    public virtual ICollection<TblApprovalChain> ApprovalChains { get; set; } = new List<TblApprovalChain>();

    [InverseProperty("Location")]
    public virtual ICollection<TblLocationAccessRule> LocationAccessRules { get; set; } = new List<TblLocationAccessRule>();

    [InverseProperty("Location")]
    public virtual ICollection<TblLocationMetadataRule> MetadataRules { get; set; } = new List<TblLocationMetadataRule>();

    [InverseProperty("Location")]
    public virtual ICollection<TblFloorPlanElement> FloorPlanElements { get; set; } = new List<TblFloorPlanElement>();

    [InverseProperty("Location")]
    public virtual ICollection<TblControlClient> ControlClients { get; set; } = new List<TblControlClient>();
}
