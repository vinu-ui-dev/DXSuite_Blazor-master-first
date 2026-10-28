using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// A BrightSign OS image held for provisioning, keyed by hardware family and version.
///
/// Images are uploaded rather than mirrored from firmware.bsn.cloud: an on-prem DXSuite may have
/// no route to it, and a provisioning feature that silently depends on outbound internet repeats
/// the network assumption this flavour exists to stop making.
/// </summary>
[Table("tblFirmwareImage")]
[Index(nameof(Family), nameof(VersionNumber), Name = "UX_tblFirmwareImage_Family_Version", IsUnique = true)]
public class TblFirmwareImage
{
    [Key]
    public int FirmwareImageId { get; set; }

    // hd4, xt4 or xt5. A single 9.1.151 image spans Series 3 to 6, but the .bsfw file is still
    // per-family, so a provisioning card is family-specific.
    [Required]
    [StringLength(10)]
    [Unicode(false)]
    public string Family { get; set; } = null!;

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string VersionNumber { get; set; } = null!;

    [Required]
    [StringLength(255)]
    [Unicode(false)]
    public string FileName { get; set; } = null!;

    [Required]
    [StringLength(500)]
    [Unicode(false)]
    public string FilePath { get; set; } = null!;

    public long? FileSize { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string? Sha256 { get; set; }

    // The DXSuite-wide default for this family, which a package may override and record.
    public bool IsDefaultForFamily { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? UploadedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime UploadedDate { get; set; }

    [InverseProperty("FirmwareImage")]
    public virtual ICollection<TblProvisioningPackage> Packages { get; set; } = new List<TblProvisioningPackage>();
}

/// <summary>
/// The BrightSign hardware families a provisioning package can target. One package is one family.
/// </summary>
public static class HardwareFamilies
{
    public const string Hd4 = "hd4";
    public const string Xt4 = "xt4";
    public const string Xt5 = "xt5";

    public static bool IsValid(string? value)
        => value is Hd4 or Xt4 or Xt5;
}
