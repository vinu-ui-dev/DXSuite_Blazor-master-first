using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblMessage")]
[Index(nameof(MessageTypeId), Name = "IX_tblMessage_MessageTypeId")]
[Index(nameof(OwningUserGroupId), Name = "IX_tblMessage_OwningUserGroupId")]
[Index(nameof(SignVideoAssetId), Name = "IX_tblMessage_SignVideoAssetId")]
public partial class TblMessage
{
    [Key]
    public int MessageId { get; set; }

    public int MessageTypeId { get; set; }

    /// <summary>Null means the message is shared by every group.</summary>
    public int? OwningUserGroupId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = null!;

    public bool IsCanned { get; set; }

    public int? SignVideoAssetId { get; set; }

    public MessageStatus Status { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? ModifiedUtc { get; set; }

    public int? ModifiedByUserId { get; set; }

    [ForeignKey("MessageTypeId")]
    [InverseProperty("Messages")]
    public virtual TblMessageType MessageType { get; set; } = null!;

    [ForeignKey("OwningUserGroupId")]
    public virtual TblUserGroup? OwningUserGroup { get; set; }

    [ForeignKey("SignVideoAssetId")]
    public virtual TblAsset? SignVideoAsset { get; set; }

    [InverseProperty("Message")]
    public virtual ICollection<TblMessageText> Texts { get; set; } = new List<TblMessageText>();

    [InverseProperty("Message")]
    public virtual ICollection<TblMessageActivation> Activations { get; set; } = new List<TblMessageActivation>();
}
