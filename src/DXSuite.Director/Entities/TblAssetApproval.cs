using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Tracks approval status for hotel-uploaded content.
/// When content approval is enabled, hotel users' uploads require admin approval before playing on devices.
/// </summary>
[Table("tblAssetApproval")]
public partial class TblAssetApproval
{
    [Key]
    public int AssetApprovalId { get; set; }

    /// <summary>
    /// Links to the uploaded asset
    /// </summary>
    public int AssetId { get; set; }

    /// <summary>
    /// 0 = Pending, 1 = Approved, 2 = Declined
    /// </summary>
    public int ApprovalStatus { get; set; } = 0;

    /// <summary>
    /// User who uploaded the content
    /// </summary>
    public int UploadedByUserId { get; set; }

    /// <summary>
    /// Admin who reviewed (approved/declined) the content
    /// </summary>
    public int? ReviewedByUserId { get; set; }

    /// <summary>
    /// When the content was uploaded/submitted for approval
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime SubmittedDate { get; set; }

    /// <summary>
    /// When the content was reviewed (approved or declined)
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? ReviewedDate { get; set; }

    /// <summary>
    /// Optional notes from the reviewer
    /// </summary>
    [StringLength(500)]
    [Unicode(false)]
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// When the content was declined. Used with DeclinedContentRetentionDays for auto-cleanup.
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? DeclinedDate { get; set; }

    /// <summary>
    /// JSON for flexible fields like metadataTag (legacy key: "hotelNumber")
    /// Example: {"metadataTag": "12345"}
    /// </summary>
    public string? ExtendedData { get; set; }

    // Navigation properties
    [ForeignKey("AssetId")]
    [InverseProperty("AssetApproval")]
    public virtual TblAsset Asset { get; set; } = null!;

    [ForeignKey("UploadedByUserId")]
    public virtual TblUser UploadedByUser { get; set; } = null!;

    [ForeignKey("ReviewedByUserId")]
    public virtual TblUser? ReviewedByUser { get; set; }

    [InverseProperty("AssetApproval")]
    public virtual ICollection<TblApprovalRequest> ApprovalRequests { get; set; } = new List<TblApprovalRequest>();
}
