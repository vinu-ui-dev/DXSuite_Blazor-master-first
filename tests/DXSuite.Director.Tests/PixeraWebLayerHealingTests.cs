using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

// The extraction rule decides which Pixera web resources may be forcibly
// re-navigated after an outage. A false positive reloads a healthy customer
// wall; a false negative leaves an error page on screen — both directions
// are pinned here.
public class PixeraWebLayerHealingTests
{
    [Theory]
    [InlineData("https://dev.dxsuite.ai/pixera/layer?locationId=5482&layers=mask&id=CMHLED011-1", "CMHLED011-1")]
    [InlineData("https://dxsuite.customer.com/videowall/layer?locationId=2162&id=WALL-EAST-3", "WALL-EAST-3")]
    [InlineData("/videowall/layer?locationId=3214&id=local.relative_1", "local.relative_1")]
    [InlineData("/pixera/counter-content?locationId=3214&id=CTR-7", "CTR-7")]
    public void WallUrls_WithValidId_Extract(string url, string expected)
    {
        Assert.Equal(expected, PixeraWebLayerHealing.ExtractMonitorId(url));
    }

    [Fact]
    public void IdParamName_IsCaseInsensitive()
    {
        Assert.Equal("X1", PixeraWebLayerHealing.ExtractMonitorId("/videowall/layer?ID=X1"));
    }

    [Fact]
    public void EncodedIdValue_IsUnescaped()
    {
        Assert.Equal("A_B", PixeraWebLayerHealing.ExtractMonitorId("/videowall/layer?id=A%5FB"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/videowall/layer?locationId=3214")]                    // no id — unmonitored, never guess
    [InlineData("/videowall/layer?id=bad%20id!")]                       // invalid monitor-id characters
    [InlineData("/somepage?id=REAL-1")]                                 // id present but not a wall path
    [InlineData("https://other.example.com/dashboard?id=REAL-1")]       // absolute non-wall path
    [InlineData("file:///C:/kiosk/start.html?id=REAL-1")]               // not a wall URL at all
    public void NonQualifyingUrls_ReturnNull(string? url)
    {
        Assert.Null(PixeraWebLayerHealing.ExtractMonitorId(url));
    }

    [Fact]
    public void PixeraPrefix_MustBeAPathSegment_NotASubstring()
    {
        Assert.Null(PixeraWebLayerHealing.ExtractMonitorId("/pixerademo/layer?id=REAL-1"));
        Assert.Null(PixeraWebLayerHealing.ExtractMonitorId("/videowalls/layer?id=REAL-1"));
    }
}
