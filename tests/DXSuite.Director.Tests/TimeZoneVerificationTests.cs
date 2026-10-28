using System.Text.Json;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class TimeZoneVerificationTests
{
    private sealed record BadgeVector(
        string Intended, decimal? ReportedJanuary, decimal? ReportedJuly, int? Year,
        bool AppliedMatches, string Expected, string Note);

    private const string IntendedLiteral = "POSIX:INTENDED";

    public static TheoryData<string, decimal?, decimal?, int?, bool, string, string> BadgeVectors()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TimeZoneVectors.json"));
        using var doc = JsonDocument.Parse(stream);

        var data = new TheoryData<string, decimal?, decimal?, int?, bool, string, string>();
        foreach (var v in doc.RootElement.GetProperty("verificationBadge").Deserialize<BadgeVector[]>(
                     new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
        {
            data.Add(v.Intended, v.ReportedJanuary, v.ReportedJuly, v.Year, v.AppliedMatches, v.Expected, v.Note);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadgeVectors))]
    public void Evaluate_matches_the_badge_vectors(
        string intended, decimal? reportedJanuary, decimal? reportedJuly, int? year,
        bool appliedMatches, string expected, string note)
    {
        var fingerprint = new TimeZoneFingerprint(
            reportedJanuary, reportedJuly, year, "POSIX:REPORTED",
            appliedMatches ? IntendedLiteral : "POSIX:SOMETHING-ELSE");

        var state = TimeZoneVerification.Evaluate(intended, IntendedLiteral, fingerprint);

        Assert.Equal(Enum.Parse<TimeZoneVerificationState>(expected), state);
        Assert.False(string.IsNullOrWhiteSpace(note));
    }

    [Fact]
    public void A_device_that_has_reported_nothing_is_not_reported()
        => Assert.Equal(
            TimeZoneVerificationState.NotReported,
            TimeZoneVerification.Evaluate("America/Phoenix", IntendedLiteral, reported: null));

    // signageOS players discard the zone and never report a fingerprint, so they sit here forever.
    [Fact]
    public void An_intended_zone_that_does_not_resolve_leaves_nothing_to_compare()
        => Assert.Equal(
            TimeZoneVerificationState.NotReported,
            TimeZoneVerification.Evaluate("UNSET", IntendedLiteral,
                new TimeZoneFingerprint(-7, -7, 2026, "POSIX:MST7", IntendedLiteral)));
}
