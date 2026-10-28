using DXSuite.Director.Services;
using static DXSuite.Director.Services.ClusterBoundsResolver;

namespace DXSuite.Director.Tests;

// This math decides mask/background/logo coverage on production walls, so every
// rule is pinned: legacy wall-edge behavior, the 50/50 same-row split, legacy
// cross-row inheritance, and the authored-bound precedence incl. neighbor-follow.
public class ClusterBoundsResolverTests
{
    // One row of four 500px counters with a 100px gap after each, starting at x=280.
    private static DisplaySlot[] SingleRow(double? allocLeft = null, double? allocRight = null)
    {
        var slots = new DisplaySlot[4];
        double x = 280;
        for (int i = 0; i < 4; i++)
        {
            slots[i] = new DisplaySlot(
                LeftPx: x, WidthPx: 500, RowKey: 0,
                IsRowFirst: i == 0, IsRowLast: i == 3,
                RowAllocationLeft: allocLeft, RowAllocationRight: allocRight,
                TrailingGap: 100);
            x += 600;
        }
        return slots;
    }

    private static ClusterRun[] Runs(params (int Start, int Count)[] runs) =>
        runs.Select(r => new ClusterRun(r.Start, r.Count)).ToArray();

    [Fact]
    public void LegacyDefaults_SingleRow()
    {
        var bounds = Resolve(Runs((0, 2), (2, 2)), SingleRow());

        // Leftmost extends to 0; rightmost ends after its trailing gap and keeps
        // the shell's viewport extension flag.
        Assert.Equal(0, bounds[0].Left);
        Assert.True(bounds[1].IsRightmost);
        Assert.False(bounds[0].IsRightmost);
        Assert.Equal(280 + 3 * 600 + 500 + 100, bounds[1].Right);

        // Same-row boundary splits the 100px gap 50/50: counter 1 ends at 1380,
        // counter 2 starts at 1480 → boundary 1430 for both sides.
        Assert.Equal(1430, bounds[0].Right);
        Assert.Equal(1430, bounds[1].Left);
    }

    [Fact]
    public void CrossRowBoundary_WithoutBounds_KeepsAbsorbLeftRule()
    {
        var row0 = SingleRow();
        var row1Start = 3000;
        var slots = row0.Concat(new[]
        {
            new DisplaySlot(row1Start, 500, RowKey: 1, IsRowFirst: true, IsRowLast: true,
                RowAllocationLeft: null, RowAllocationRight: null, TrailingGap: 0),
        }).ToArray();

        var bounds = Resolve(Runs((0, 4), (4, 1)), slots);

        // The whole inter-row gap belongs to the airline on the left.
        Assert.Equal(row1Start, bounds[0].Right);
        Assert.Equal(row1Start, bounds[1].Left);
    }

    [Fact]
    public void RowRight_PinsEdge_AndNeighborFollows()
    {
        var slots = SingleRow(allocRight: 2600).Concat(new[]
        {
            new DisplaySlot(3000, 500, RowKey: 1, IsRowFirst: true, IsRowLast: true,
                RowAllocationLeft: null, RowAllocationRight: null, TrailingGap: 0),
        }).ToArray();

        var bounds = Resolve(Runs((0, 4), (4, 1)), slots);

        Assert.Equal(2600, bounds[0].Right);
        Assert.Equal(2600, bounds[1].Left);
    }

    [Fact]
    public void NextRowLeft_PinsEdge_AndNeighborFollows()
    {
        var slots = SingleRow().Concat(new[]
        {
            new DisplaySlot(3000, 500, RowKey: 1, IsRowFirst: true, IsRowLast: true,
                RowAllocationLeft: 2700, RowAllocationRight: null, TrailingGap: 0),
        }).ToArray();

        var bounds = Resolve(Runs((0, 4), (4, 1)), slots);

        Assert.Equal(2700, bounds[0].Right);
        Assert.Equal(2700, bounds[1].Left);
    }

    [Fact]
    public void BothSidesAuthored_ApplyVerbatim_AllowingGaps()
    {
        var slots = SingleRow(allocRight: 2500).Concat(new[]
        {
            new DisplaySlot(3000, 500, RowKey: 1, IsRowFirst: true, IsRowLast: true,
                RowAllocationLeft: 2800, RowAllocationRight: null, TrailingGap: 0),
        }).ToArray();

        var bounds = Resolve(Runs((0, 4), (4, 1)), slots);

        // Intentional 300px uncovered strip between the rows.
        Assert.Equal(2500, bounds[0].Right);
        Assert.Equal(2800, bounds[1].Left);
    }

    [Fact]
    public void AuthoredWallEdges_OverrideExtensionRules()
    {
        var bounds = Resolve(Runs((0, 4)), SingleRow(allocLeft: 100, allocRight: 2500));

        Assert.Equal(100, bounds[0].Left);           // not extended to 0
        Assert.Equal(2500, bounds[0].Right);         // not last display + gap
        Assert.False(bounds[0].IsRightmost);         // shell must not re-extend
    }

    [Fact]
    public void MidRunBounds_OnNonEdgeCounters_AreIgnored()
    {
        // Bounds authored on the row, but the boundary sits mid-row (neither slot
        // is a row edge) → 50/50 applies, not the row bounds.
        var bounds = Resolve(Runs((0, 2), (2, 2)), SingleRow(allocLeft: 100, allocRight: 2500));

        Assert.Equal(1430, bounds[0].Right);
        Assert.Equal(1430, bounds[1].Left);
    }
}
