namespace DXSuite.Director.Services;

/// <summary>
/// Pure cluster-boundary math for the video wall allocation: given the ordered
/// counter display geometry and the airline cluster runs over it, resolves each
/// cluster's coverage extents. Extracted from PixeraAllocationService so the
/// rules that decide mask/background/logo coverage are unit-testable.
///
/// Rules, in precedence order at each edge:
///  1. An authored row bound (AllocationLeftPx on the row's first counter /
///     AllocationRightPx on the row's last counter) pins that edge. When only one
///     side of a shared boundary is authored, the neighbor follows it so coverage
///     stays contiguous; when both are authored they apply verbatim (gaps and
///     overlaps are intentional — uncovered canvas shows wall-level layers).
///  2. Same-row boundaries (an airline transition inside one counter row) split
///     the physical gap 50/50 between the two clusters.
///  3. Cross-row boundaries keep the legacy rule: the gap belongs to the airline
///     on the left (right edge extends to the next cluster's first display).
///  4. Wall edges: the leftmost cluster extends to x=0 (lead-in space); the
///     rightmost ends after its trailing gap and is flagged IsRightmost so the
///     layer shell extends it to the viewport edge — unless an authored right
///     bound pins it, which also clears the flag.
/// </summary>
internal static class ClusterBoundsResolver
{
    /// <summary>One counter display in ordered-canvas-X sequence.</summary>
    internal readonly record struct DisplaySlot(
        double LeftPx,
        double WidthPx,
        /// <summary>Identity of the owning counter row; equality defines "same row".</summary>
        int RowKey,
        bool IsRowFirst,
        bool IsRowLast,
        double? RowAllocationLeft,
        double? RowAllocationRight,
        /// <summary>Gap after this display from the row's cyclic pattern.</summary>
        double TrailingGap);

    internal readonly record struct ClusterRun(int StartIndex, int Count);

    internal readonly record struct ClusterBounds(double Left, double Right, bool IsRightmost);

    internal static IReadOnlyList<ClusterBounds> Resolve(
        IReadOnlyList<ClusterRun> runs, IReadOnlyList<DisplaySlot> slots)
    {
        var lefts  = new double[runs.Count];
        var rights = new double[runs.Count];

        for (int k = 0; k < runs.Count - 1; k++)
        {
            var end   = slots[runs[k].StartIndex + runs[k].Count - 1];
            var start = slots[runs[k + 1].StartIndex];

            double? authoredRight = end.IsRowLast ? end.RowAllocationRight : null;
            double? authoredLeft  = start.IsRowFirst ? start.RowAllocationLeft : null;

            if (authoredRight.HasValue && authoredLeft.HasValue)
            {
                rights[k]    = authoredRight.Value;
                lefts[k + 1] = authoredLeft.Value;
            }
            else if (authoredRight.HasValue)
            {
                rights[k] = lefts[k + 1] = authoredRight.Value;
            }
            else if (authoredLeft.HasValue)
            {
                rights[k] = lefts[k + 1] = authoredLeft.Value;
            }
            else if (end.RowKey == start.RowKey)
            {
                double midpoint = (end.LeftPx + end.WidthPx + start.LeftPx) / 2.0;
                rights[k] = lefts[k + 1] = midpoint;
            }
            else
            {
                rights[k] = lefts[k + 1] = start.LeftPx;
            }
        }

        var first = slots[runs[0].StartIndex];
        lefts[0] = first.IsRowFirst && first.RowAllocationLeft.HasValue
            ? first.RowAllocationLeft.Value
            : Math.Min(first.LeftPx, 0);

        var lastRun = runs[^1];
        var last = slots[lastRun.StartIndex + lastRun.Count - 1];
        bool lastPinned = last.IsRowLast && last.RowAllocationRight.HasValue;
        rights[^1] = lastPinned
            ? last.RowAllocationRight!.Value
            : last.LeftPx + last.WidthPx + last.TrailingGap;

        var bounds = new ClusterBounds[runs.Count];
        for (int k = 0; k < runs.Count; k++)
            bounds[k] = new ClusterBounds(lefts[k], rights[k],
                IsRightmost: k == runs.Count - 1 && !lastPinned);
        return bounds;
    }
}
