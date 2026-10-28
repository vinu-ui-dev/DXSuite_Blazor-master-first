using System.Text.Json;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class DailyWindowTests
{
    private sealed record WindowVector(string? Start, string? End, string LocalTime, bool Expected, string Note);

    public static TheoryData<string?, string?, string, bool, string> WindowRuleVectors()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "TimeZoneVectors.json"));
        using var doc = JsonDocument.Parse(stream);

        var data = new TheoryData<string?, string?, string, bool, string>();
        foreach (var vector in doc.RootElement.GetProperty("windowRule").Deserialize<WindowVector[]>(
                     new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
        {
            data.Add(vector.Start, vector.End, vector.LocalTime, vector.Expected, vector.Note);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WindowRuleVectors))]
    public void Contains_matches_the_window_rule(string? start, string? end, string localTime, bool expected, string note)
    {
        var window = new DailyWindow(ParseTime(start), ParseTime(end));

        Assert.Equal(expected, window.Contains(TimeOnly.Parse(localTime)));
        Assert.False(string.IsNullOrWhiteSpace(note));
    }

    // The two exceptions cannot interact: wrapping needs a start later than the end, which no end
    // at or after 23:59 permits. Asserted rather than left to the reader of the rule.
    [Theory]
    [InlineData("23:59")]
    [InlineData("23:59:59")]
    public void An_end_at_or_after_2359_can_never_also_wrap(string end)
    {
        var endTime = TimeOnly.Parse(end);

        Assert.All(
            Enumerable.Range(0, 24).Select(h => new TimeOnly(h, 0)),
            start => Assert.False(start > endTime));
    }

    private static TimeOnly? ParseTime(string? value)
        => value is null ? null : TimeOnly.Parse(value);
}
