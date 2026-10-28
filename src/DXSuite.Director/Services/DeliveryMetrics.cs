using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;

namespace DXSuite.Director.Services;

/// <summary>
/// Instruments the player poll path's delivery pass. Nothing exports these: read them on the web node with
/// <c>dotnet-counters monitor --counters DXSuite.Director.Delivery -p &lt;pid&gt;</c>, or attach an exporter to
/// the meter by name. See docs/spec/metadata-content-targeting.md section 15.7.
/// </summary>
public sealed class DeliveryMetrics
{
    public const string MeterName = "DXSuite.Director.Delivery";

    public const string PlaylistKind = "playlist";
    public const string DisplayKind = "display";
    public const string LayoutKind = "layout";

    private readonly Histogram<double> _passDuration;
    private readonly ObservableGauge<int> _combinations;
    private readonly DistinctCombinationWindow _window = new(DistinctCombinationWindow.DefaultLifetime);

    public DeliveryMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _passDuration = meter.CreateHistogram<double>(
            "dxsuite.delivery.pass.duration", "ms",
            "Time to resolve one device's delivery from its cached publication. Playlist and display passes are in-memory work; a layout pass also waits on database queries for its card zones, so compare it only with itself.");
        _combinations = meter.CreateObservableGauge(
            "dxsuite.delivery.metadata_combinations", ObserveCombinations, "{combination}",
            "Distinct device metadata combinations each targeting location resolved for in the last 15 minutes. Above 1 means delivery is resolving per device.");
    }

    public long StartPass() => Stopwatch.GetTimestamp();

    public void EndPass(string kind, long startTimestamp) =>
        _passDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, new KeyValuePair<string, object?>("kind", kind));

    /// <summary>
    /// Counts the device's combination of values for a targeting location's participating keys. Skipped while
    /// nothing listens, so the hash costs nothing on an unobserved node.
    /// </summary>
    public void RecordCombination(string tenantId, int locationId, LocationTargeting targeting, IReadOnlyDictionary<string, string>? deviceMetadata)
    {
        if (!_combinations.Enabled || !targeting.Enabled || targeting.Rules.Participating.Count == 0) return;

        var hash = MetadataCombination.Hash(
            targeting.Rules.Participating.Select(r => r.Key),
            MetadataSubjectResolver.DeviceSubject(deviceMetadata));
        _window.Record(new CombinationLocation(tenantId, locationId), hash, DateTime.UtcNow);
    }

    private IEnumerable<Measurement<int>> ObserveCombinations() =>
        _window.Counts(DateTime.UtcNow).Select(kv => new Measurement<int>(kv.Value,
            new KeyValuePair<string, object?>("tenant", kv.Key.TenantId),
            new KeyValuePair<string, object?>("location_id", kv.Key.LocationId)));
}

public readonly record struct CombinationLocation(string TenantId, int LocationId);

/// <summary>
/// A short, stable fingerprint of a device's values for a location's participating keys. Observability only:
/// it keys no cache and nothing reads it back, which is what separates it from the variant key spec section 8.2
/// deleted.
/// </summary>
public static class MetadataCombination
{
    public static string Hash(IEnumerable<string> participatingKeys, IReadOnlyDictionary<string, IReadOnlySet<string>> deviceValues)
    {
        var canonical = new StringBuilder();
        foreach (var key in participatingKeys
                     .Select(MetadataEvaluator.NormalizeKey)
                     .Where(k => k.Length > 0)
                     .Distinct(MetadataEvaluator.KeyComparer)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var values = deviceValues
                .Where(kv => MetadataEvaluator.KeyComparer.Equals(MetadataEvaluator.NormalizeKey(kv.Key), key))
                .SelectMany(kv => kv.Value)
                .Select(v => v.Trim().ToUpperInvariant())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            canonical.Append(key.ToUpperInvariant()).Append('=').AppendJoin('', values).Append('');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())), 0, 6);
    }
}

/// <summary>
/// The distinct combinations each location saw within a rolling lifetime. A combination drops out once its
/// last sighting is older than the lifetime, so the count describes current resolution rather than growing
/// until the process recycles.
/// </summary>
public sealed class DistinctCombinationWindow
{
    /// <summary>Five polls at the normal 180 second cadence, so every connected player is counted.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _lifetime;
    private readonly Dictionary<CombinationLocation, Dictionary<string, DateTime>> _lastSeen = new();
    private readonly object _gate = new();
    private DateTime _lastPrune = DateTime.MinValue;

    public DistinctCombinationWindow(TimeSpan lifetime) => _lifetime = lifetime;

    public void Record(CombinationLocation location, string hash, DateTime utcNow)
    {
        lock (_gate)
        {
            if (!_lastSeen.TryGetValue(location, out var hashes))
                _lastSeen[location] = hashes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            hashes[hash] = utcNow;

            // A listener that enables the meter but never collects this gauge would otherwise let sightings pile
            // up with every retag, since collection is the only other place they expire.
            if (utcNow - _lastPrune >= _lifetime)
                PruneLocked(utcNow);
        }
    }

    /// <summary>Prunes expired sightings and returns each location still holding at least one.</summary>
    public IReadOnlyDictionary<CombinationLocation, int> Counts(DateTime utcNow)
    {
        lock (_gate)
        {
            PruneLocked(utcNow);
            return _lastSeen.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        }
    }

    private void PruneLocked(DateTime utcNow)
    {
        _lastPrune = utcNow;
        var cutoff = utcNow - _lifetime;
        foreach (var (location, hashes) in _lastSeen.ToList())
        {
            foreach (var expired in hashes.Where(h => h.Value < cutoff).Select(h => h.Key).ToList())
                hashes.Remove(expired);
            if (hashes.Count == 0)
                _lastSeen.Remove(location);
        }
    }
}
