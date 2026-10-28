using System.Diagnostics.Metrics;
using DXSuite.Director.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Tests;

/// <summary>
/// The delivery instrument's metadata dimension. It must stay bounded, so the combination is a stable short hash
/// reported as a distinct count per location over a rolling window, never as a dimension of its own.
/// </summary>
public class DeliveryMetricsTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Device(params string[] tags) =>
        MetadataSubjectResolver.GroupValues(tags.Select(t => t.Split('=')).Select(p => (p[0], p[1])));

    private static LocationTargeting Targeting(bool enabled, params string[] keys) =>
        new(enabled, new LocationMetadataRules(MetadataCombineMode.All, keys.Select(k => new MetadataKeyRule(k)), keys));

    [Fact]
    public void Hash_IgnoresKeyOrderCaseAndWhitespace()
    {
        var a = MetadataCombination.Hash(["Carrier", "Terminal"], Device("carrier= Delta ", "TERMINAL=T1"));
        var b = MetadataCombination.Hash([" terminal", "CARRIER"], Device("Terminal=t1", "Carrier=delta"));

        Assert.Equal(a, b);
        Assert.Equal(12, a.Length);
    }

    [Fact]
    public void Hash_ReadsOnlyParticipatingKeys()
    {
        var a = MetadataCombination.Hash(["Carrier"], Device("Carrier=Delta", "Gate=B4"));
        var b = MetadataCombination.Hash(["Carrier"], Device("Carrier=Delta", "Gate=C9"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_TellsValuesAndBlankKeysApart()
    {
        var delta = MetadataCombination.Hash(["Carrier", "Terminal"], Device("Carrier=Delta"));
        var united = MetadataCombination.Hash(["Carrier", "Terminal"], Device("Carrier=United"));
        var shifted = MetadataCombination.Hash(["Carrier", "Terminal"], Device("Terminal=Delta"));

        Assert.NotEqual(delta, united);
        Assert.NotEqual(delta, shifted);
    }

    [Fact]
    public void Window_CountsDistinctCombinationsPerLocation_AndForgetsStaleOnes()
    {
        var window = new DistinctCombinationWindow(TimeSpan.FromMinutes(15));
        var start = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        var gate = new CombinationLocation("t1", 22);
        var lobby = new CombinationLocation("t1", 23);

        window.Record(gate, "aaa", start);
        window.Record(gate, "bbb", start.AddMinutes(5));
        window.Record(gate, "aaa", start.AddMinutes(10));
        window.Record(lobby, "aaa", start);

        Assert.Equal(2, window.Counts(start.AddMinutes(12))[gate]);

        var later = window.Counts(start.AddMinutes(19));
        Assert.Equal(2, later[gate]);
        Assert.False(later.ContainsKey(lobby));

        Assert.Equal(1, window.Counts(start.AddMinutes(21))[gate]);
        Assert.Empty(window.Counts(start.AddMinutes(26)));
    }

    [Fact]
    public void Window_KeepsTenantsApart()
    {
        var window = new DistinctCombinationWindow(TimeSpan.FromMinutes(15));
        var now = DateTime.UtcNow;

        window.Record(new CombinationLocation("t1", 22), "aaa", now);
        window.Record(new CombinationLocation("t2", 22), "bbb", now);

        Assert.Equal(2, window.Counts(now).Count);
    }

    [Fact]
    public void Metrics_PublishPassDurationAndCombinationCounts_OnlyWhileListenedTo()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var metrics = new DeliveryMetrics(factory);
        var targeting = Targeting(true, "Carrier");

        // Nothing listens yet, so this combination is never hashed or counted.
        metrics.RecordCombination("t1", 22, targeting, new Dictionary<string, string> { ["Carrier"] = "WestJet" });

        var durations = new List<double>();
        var combinations = new List<(int Value, int LocationId)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            // Scoped to this test's factory, so a meter another test creates under the same name is ignored.
            if (instrument.Meter.Name == DeliveryMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Add(value));
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
            combinations.Add((value, (int)tags.ToArray().Single(t => t.Key == "location_id").Value!)));
        listener.Start();

        metrics.EndPass(DeliveryMetrics.PlaylistKind, metrics.StartPass());
        metrics.RecordCombination("t1", 22, targeting, new Dictionary<string, string> { ["Carrier"] = "Delta" });
        metrics.RecordCombination("t1", 22, targeting, new Dictionary<string, string> { ["carrier"] = "delta" });
        metrics.RecordCombination("t1", 22, targeting, new Dictionary<string, string> { ["Carrier"] = "United" });
        metrics.RecordCombination("t1", 23, Targeting(false, "Carrier"), new Dictionary<string, string> { ["Carrier"] = "Delta" });
        listener.RecordObservableInstruments();

        Assert.Single(durations);
        Assert.Equal(new[] { (2, 22) }, combinations);
    }
}
