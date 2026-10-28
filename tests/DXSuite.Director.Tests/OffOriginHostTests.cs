using System.Reflection;
using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// What counts as a host, and what counts as off-origin. The report is a site survey, so an
/// under-report is the failure that matters: it passes, the firewall request is written, and the
/// site breaks later.
/// </summary>
public class OffOriginHostTests
{
    private static string? TryGetHost(string? url)
        => (string?)typeof(OffOriginHostReportService)
            .GetMethod("TryGetHost", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [url]);

    [Theory]
    [InlineData("https://dashboards.example.com/board", "dashboards.example.com")]
    [InlineData("http://192.168.4.25/page", "192.168.4.25")]
    [InlineData("  https://example.com/x  ", "example.com")]
    public void AnAbsoluteHttpUrlYieldsItsHost(string url, string expected)
    {
        Assert.Equal(expected, TryGetHost(url));
    }

    [Fact]
    public void ANonDefaultPortIsPartOfTheHost()
    {
        // A firewall rule is host and port, and on-prem content routinely sits on an odd port.
        Assert.Equal("example.com:8080", TryGetHost("http://example.com:8080/x"));
        Assert.Equal("example.com", TryGetHost("https://example.com:443/x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/MediaAssets/12/page.html")]
    [InlineData("relative/path.html")]
    public void ARelativeOrAbsentPathIsNotAHost(string? url)
    {
        // Same-origin by construction, so nothing for a firewall to allow.
        Assert.Null(TryGetHost(url));
    }

    [Theory]
    [InlineData("file:///SD:/app/a/index.html")]
    [InlineData("ftp://example.com/x")]
    [InlineData("rtsp://example.com/stream")]
    public void ANonHttpSchemeIsNotReported(string url)
    {
        Assert.Null(TryGetHost(url));
    }

    [Fact]
    public void AnIpAndAHostnameForTheSameServerAreDifferentHosts()
    {
        // Deliberate: on-prem, that mismatch is itself something a site survey should surface
        // rather than normalise away.
        Assert.NotEqual(TryGetHost("https://dxsuite.example.com/x"), TryGetHost("https://192.168.4.25/x"));
    }
}
