using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// Builds the small content world a metadata targeting test asserts on: one playlist location's rules,
/// its publication, and the players polling it. Each test builds its own world, so adding an asset for
/// one test can never change the expected set of another.
/// </summary>
internal sealed class MetadataWorldBuilder
{
    public const int LocationId = 22;

    private readonly List<PlayerContentItem> _assets = new();
    private readonly List<MetadataKeyRule> _rules = new();
    private MetadataCombineMode _combine = MetadataCombineMode.All;
    private bool _targeting;
    private bool _shuffle;

    /// <summary>Switches targeting on with these participating keys, each registered.</summary>
    public MetadataWorldBuilder TargetedBy(params MetadataKeyRule[] rules)
    {
        _targeting = true;
        _rules.AddRange(rules);
        return this;
    }

    /// <summary>Keeps the rules but leaves the location's "Target content by metadata" switch off.</summary>
    public MetadataWorldBuilder WithTargetingSwitchedOff()
    {
        _targeting = false;
        return this;
    }

    public MetadataWorldBuilder CombinedBy(MetadataCombineMode combine)
    {
        _combine = combine;
        return this;
    }

    public MetadataWorldBuilder Shuffled()
    {
        _shuffle = true;
        return this;
    }

    /// <summary>
    /// Adds an asset in play order, named by its file name so an exact-set assertion reads as a list of
    /// names. <paramref name="tags"/> are "Key=Value" pairs; a key given twice holds two values.
    /// </summary>
    public MetadataWorldBuilder WithAsset(
        string name, string[]? tags = null, TimeSpan? startTime = null, TimeSpan? endTime = null, int? weight = null,
        bool noAssetRow = false)
    {
        _assets.Add(new PlayerContentItem
        {
            FileName = name,
            Url = $"/MediaAssets/{LocationId}/{name}",
            PlayOrder = _assets.Count + 1,
            MetadataValues = noAssetRow ? null : Values(tags ?? []),
            StartTime = startTime,
            EndTime = endTime,
            Weight = weight
        });
        return this;
    }

    public CachedManifest BuildPublication() => new()
    {
        LocationId = LocationId,
        Status = "Success",
        Shuffle = _shuffle,
        EnableFade = true,
        Assets = _assets.ToList()
    };

    public LocationTargeting BuildTargeting() => new(
        _targeting,
        new LocationMetadataRules(_combine, _rules, _rules.Select(r => r.Key)));

    public static Dictionary<string, string> Player(params string[] tags)
        => tags.Select(Split).ToDictionary(t => t.Key, t => t.Value);

    /// <summary>
    /// A generated volume world: <paramref name="assets"/> assets over three keys, a third of them untagged,
    /// every fourth windowed, weights 1 to 5. Deterministic, so two runs measure the same work.
    /// </summary>
    public static MetadataWorldBuilder Volume(int assets, bool shuffle)
    {
        var world = new MetadataWorldBuilder().TargetedBy(
            new MetadataKeyRule("Concourse"),
            new MetadataKeyRule("Airline"),
            new MetadataKeyRule("Language"));
        if (shuffle)
            world.Shuffled();

        for (var i = 0; i < assets; i++)
        {
            string[] tags = (i % 3) switch
            {
                0 => [],
                1 => [$"Concourse=Concourse {(char)('A' + i % 4)}", $"Airline=Airline {i % 5}"],
                _ => [$"Concourse=Concourse {(char)('A' + i % 4)}", $"Concourse=Concourse {(char)('A' + (i + 1) % 4)}", $"Language=Lang {i % 2}"]
            };
            world.WithAsset(
                $"asset-{i:D4}.png",
                tags,
                startTime: i % 4 == 0 ? new TimeSpan(9, 0, 0) : null,
                endTime: i % 4 == 0 ? new TimeSpan(17, 0, 0) : null,
                weight: i % 5 + 1);
        }

        return world;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(IEnumerable<string> tags) =>
        tags.Select(Split)
            .GroupBy(t => t.Key, MetadataEvaluator.KeyComparer)
            .ToDictionary(g => g.Key, g => MetadataEvaluator.NormalizeValues(g.Select(t => t.Value)), MetadataEvaluator.KeyComparer);

    private static (string Key, string Value) Split(string tag)
    {
        var at = tag.IndexOf('=');
        return (tag[..at], tag[(at + 1)..]);
    }
}
