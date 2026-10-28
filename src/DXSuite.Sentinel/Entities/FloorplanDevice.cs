using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelFloorplanDevice")]
public partial class FloorplanDevice
{
    [Key]
    [Column("FloorplanDeviceID")]
    public int FloorplanDeviceId { get; set; }

    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("LocationID")]
    public int LocationId { get; set; }

    [Column("PositionX")]
    public double PositionX { get; set; }

    [Column("PositionY")]
    public double PositionY { get; set; }

    [Column("Rotation")]
    public double Rotation { get; set; }

    [Column("Scale")]
    public double Scale { get; set; }

    [Column("Label")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Label { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("DeviceInstanceId")]
    [InverseProperty("FloorplanDevices")]
    public virtual DeviceInstance DeviceInstance { get; set; } = null!;
}
