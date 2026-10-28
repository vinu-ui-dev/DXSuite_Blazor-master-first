namespace DXSuite.Director.Services;

/// <summary>
/// Orders the dotted numeric version strings this pipeline carries, for the one comparison that
/// has to be an ordering rather than an equality: whether a device's bootloader satisfies an app
/// bundle's MinBootloaderVersion.
///
/// Segments are compared numerically, so 9.1.151 sorts above 9.1.99 where a string comparison puts
/// it below. A missing segment reads as zero, so 1.2 and 1.2.0 are equal. Anything that does not
/// parse is treated as unknown rather than as zero: a bundle must not be offered to a device whose
/// bootloader version could not be read, because "unknown" and "old enough to brick it" look the
/// same from here.
/// </summary>
public static class VersionOrdering
{
    /// <summary>
    /// Returns true when <paramref name="candidate"/> is at least <paramref name="minimum"/>.
    /// An empty minimum is satisfied by anything. An unparseable or absent candidate satisfies
    /// nothing, which is the safe direction.
    /// </summary>
    public static bool Satisfies(string? candidate, string? minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        if (!TryParse(minimum, out var required)) return true;
        if (!TryParse(candidate, out var have)) return false;

        return Compare(have, required) >= 0;
    }

    /// <summary>Orders version strings low to high. Unparseable values sort last.</summary>
    public static IComparer<string?> Comparer { get; } = new SegmentComparer();

    private sealed class SegmentComparer : IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            var hasX = TryParse(x, out var left);
            var hasY = TryParse(y, out var right);
            if (!hasX && !hasY) return string.CompareOrdinal(x, y);
            if (!hasX) return 1;
            if (!hasY) return -1;
            return VersionOrdering.Compare(left, right);
        }
    }

    public static bool TryParse(string? value, out int[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('.');
        var parsed = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out parsed[i]) || parsed[i] < 0)
                return false;
        }

        segments = parsed;
        return true;
    }

    private static int Compare(int[] left, int[] right)
    {
        var length = Math.Max(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var l = i < left.Length ? left[i] : 0;
            var r = i < right.Length ? right[i] : 0;
            if (l != r) return l.CompareTo(r);
        }

        return 0;
    }
}
