namespace DXSuite.Director.Services.WallPlatforms;

/// <summary>
/// One group's worth of counter displays within a multi-span resolved cluster.
/// </summary>
public record ClusterSpan(string GroupName, int StartIndex, int EndIndex);

/// <summary>
/// One airline's allocation at a VideoWall location, potentially spanning multiple PanoramaGroups.
/// Null UserGroupId indicates an unassigned gap — airport fallback content should fill it.
/// </summary>
public class ResolvedCluster
{
    /// <summary>
    /// Composite key encoding all spans: "{g}:{s}-{e}" for a single group,
    /// or "{g1}:{s1}-{e1}|{g2}:{s2}-{e2}|..." for multi-group.
    /// Stable as long as group names and display indices don't shift.
    /// </summary>
    public string ClusterKey { get; init; } = "";

    /// <summary>Primary (first) span's group name — kept for backward-compatible callers.</summary>
    public string GroupName { get; init; } = "";

    /// <summary>Primary span start index.</summary>
    public int StartIndex { get; init; }

    /// <summary>Last span end index.</summary>
    public int EndIndex { get; init; }

    /// <summary>
    /// All group spans this cluster covers, in layout order.
    /// Single-group clusters have one entry; cross-group airline allocations have one per group.
    /// </summary>
    public IReadOnlyList<ClusterSpan> Spans { get; init; } = Array.Empty<ClusterSpan>();

    /// <summary>All ordered DisplayIds across all spans.</summary>
    public IReadOnlyList<int> DisplayIds { get; init; } = Array.Empty<int>();

    /// <summary>Null for unassigned gaps.</summary>
    public int? UserGroupId { get; init; }

    /// <summary>
    /// Resolved constraint: per-event override → group default → location GlobalConstraintId → null.
    /// </summary>
    public int? ResolvedConstraintId { get; init; }

    public string BackgroundMode { get; init; } = "fill-width";

    /// <summary>Airline-specific background URL or file path. Null = use parent wall's background.</summary>
    public string? BackgroundPath { get; init; }

    /// <summary>Airline-specific mask overlay URL or file path. Null = no mask.</summary>
    public string? MaskPath { get; init; }

    public string MaskMode { get; init; } = "fill-width";

    /// <summary>Airline-specific logo URL or file path for role-less zones. Null = no logo layer.</summary>
    public string? LogoPath { get; init; }

    /// <summary>Per-role logo content keyed by the wall's logo role keys; empty when the airline has none.</summary>
    public IReadOnlyDictionary<string, AirlineLogoContent> Logos { get; init; } = AirlineWallDefaults.NoLogos;
}
