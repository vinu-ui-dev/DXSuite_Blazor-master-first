namespace DXSuite.Director.Services;

/// <summary>
/// Expands a weighted rotation into the literal play sequence a player loops over.
/// Uses smooth weighted round-robin so repeats are spread through the loop instead of
/// playing back to back, and the output is deterministic for a given input, which keeps
/// manifest version hashes stable between rebuilds.
/// </summary>
public static class WeightedLoopExpander
{
    public const int MinWeight = 1;
    public const int MaxWeight = 10;

    public static List<T> Expand<T>(IReadOnlyList<T> items, Func<T, int?> weightOf)
    {
        var weights = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
            weights[i] = Math.Clamp(weightOf(items[i]) ?? MinWeight, MinWeight, MaxWeight);
        return Expand(items, weights);
    }

    public static List<T> Expand<T>(IReadOnlyList<T> items, IReadOnlyList<int> weights)
    {
        if (items.Count != weights.Count)
            throw new ArgumentException("Weight count must match item count.", nameof(weights));

        var total = 0;
        var clamped = new int[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            clamped[i] = Math.Clamp(weights[i], MinWeight, MaxWeight);
            total += clamped[i];
        }

        if (total == items.Count)
            return items.ToList();

        // Smooth weighted round-robin: each item appears exactly weight-i times over the
        // loop; first index wins ties so the sequence is stable.
        var current = new int[items.Count];
        var result = new List<T>(total);
        for (var step = 0; step < total; step++)
        {
            var best = -1;
            for (var i = 0; i < items.Count; i++)
            {
                current[i] += clamped[i];
                if (best < 0 || current[i] > current[best]) best = i;
            }
            current[best] -= total;
            result.Add(items[best]);
        }

        return result;
    }
}
