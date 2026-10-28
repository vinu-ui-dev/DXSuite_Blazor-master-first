using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Keyless]
public partial class ViewActiveDisplayContent
{
    public int DisplayId { get; set; }

    public int? AssetId { get; set; }

    public double Duration { get; set; }

    public int? SortOrder { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? AssetPath { get; set; }

    public int? AssetTypeId { get; set; }
}
