using System.Globalization;
using System.Text.Json;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class DeviceLocalTimeTests
{
    private sealed record ConversionVector(string Instant, string Zone, string? ExpectedLocal, string Note);

    public static TheoryData<string, string, string?, string> ConversionVectors()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TimeZoneVectors.json"));
        using var doc = JsonDocument.Parse(stream);

        var data = new TheoryData<string, string, string?, string>();
        foreach (var vector in doc.RootElement.GetProperty("conversion").Deserialize<ConversionVector[]>(
                     new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
        {
            data.Add(vector.Instant, vector.Zone, vector.ExpectedLocal, vector.Note);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ConversionVectors))]
    public void TryResolve_matches_the_pinned_offsets(string instant, string zone, string? expectedLocal, string note)
    {
        var resolved = DeviceLocalTime.TryResolve(
            DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            zone,
            out var localTime);

        if (expectedLocal is null)
        {
            Assert.False(resolved, note);
        }
        else
        {
            Assert.True(resolved, note);
            Assert.Equal(DateTime.Parse(expectedLocal, CultureInfo.InvariantCulture), localTime);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNSET")]
    [InlineData("Not/AZone")]
    public void TryResolve_declines_rather_than_throwing(string? timeZoneId)
    {
        Assert.False(DeviceLocalTime.TryResolve(DateTimeOffset.UtcNow, timeZoneId, out _));
    }

    // .NET on Windows resolves Windows identifiers as readily as IANA ones, so a Windows id that
    // leaked into the column would keep working here and break only on a non-Windows host. What
    // keeps the column IANA is DeviceProvisioning converting at the seed, not resolution rejecting
    // it later. Pinned so the gap stays visible rather than being rediscovered.
    [Fact]
    public void A_windows_identifier_still_resolves_on_a_windows_host()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(DeviceLocalTime.TryResolve(DateTimeOffset.UtcNow, "Eastern Standard Time", out _));
    }
}
