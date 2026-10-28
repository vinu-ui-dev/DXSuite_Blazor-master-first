using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

public class PublicationUrlsTests
{
    private const string Site = "https://tenant.example.com";
    private static string Media() => "https://tenant.example.com/MediaAssets/_t/token123";

    [Fact]
    public void MediaPlaceholder_TakesTheRequestsMediaBase()
    {
        var url = PublicationUrls.Resolve($"{PublicationUrls.MediaBase}/22/clip%20one.mp4", Site, Media);

        Assert.Equal("https://tenant.example.com/MediaAssets/_t/token123/22/clip%20one.mp4", url);
    }

    [Fact]
    public void SitePlaceholder_TakesTheRequestsSiteBase()
    {
        var url = PublicationUrls.Resolve($"{PublicationUrls.SiteBase}/MediaAssets/22/a.png", Site, Media);

        Assert.Equal("https://tenant.example.com/MediaAssets/22/a.png", url);
    }

    [Fact]
    public void NullUrl_IsReturnedUnchanged()
    {
        Assert.Null(PublicationUrls.Resolve(null!, Site, Media));
    }

    [Fact]
    public void TwoHosts_ResolveTheSamePublicationToTheirOwnBase()
    {
        const string published = PublicationUrls.MediaBase + "/22/a.png";

        Assert.Equal("https://a.example.com/m/22/a.png", PublicationUrls.Resolve(published, "https://a.example.com", () => "https://a.example.com/m"));
        Assert.Equal("http://b.example.com/m/22/a.png", PublicationUrls.Resolve(published, "http://b.example.com", () => "http://b.example.com/m"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/embed/x")]
    [InlineData("rtsp://camera.local/stream")]
    [InlineData("")]
    public void OtherUrls_AreUnchanged_AndNeverTouchTheMediaToken(string url)
    {
        var resolved = PublicationUrls.Resolve(url, Site, () => throw new InvalidOperationException("token read"));

        Assert.Equal(url, resolved);
    }
}
