using DXSuite.Director.Models;

namespace DXSuite.Director.Tests;

public class LayerRenderModeTests
{
    [Theory]
    [InlineData("fill-width",  "background-size:100% auto")]
    [InlineData("fill-height", "background-size:auto 100%")]
    [InlineData("fit",         "background-size:contain")]
    [InlineData("native",      "background-size:auto")]
    [InlineData(null,          "background-size:auto")]
    public void ImageCss_MapsModeToBackgroundSize(string? mode, string expected)
    {
        var css = LayerRenderMode.ImageCss("/MediaAssets/AirlineContent/x.png", mode);

        Assert.Contains(expected, css);
        Assert.Contains("background-image:url('/MediaAssets/AirlineContent/x.png')", css);
    }

    [Fact]
    public void ImageCss_Tile_Repeats()
    {
        var css = LayerRenderMode.ImageCss("/x.png", "tile");

        // A trailing no-repeat after the mode switch used to defeat tiling entirely.
        Assert.Contains("background-repeat:repeat", css);
        Assert.DoesNotContain("no-repeat", css);
    }

    [Theory]
    [InlineData("fill-width",  "height:auto;")]
    [InlineData("fill-height", "width:auto;")]
    [InlineData("fit",         "object-fit:contain;")]
    [InlineData("tile",        "width:auto; height:auto;")]
    [InlineData("native",      "width:auto; height:auto;")]
    [InlineData(null,          "width:auto; height:auto;")]
    public void VideoCss_MapsModeToSizing(string? mode, string expected) =>
        Assert.Equal(expected, LayerRenderMode.VideoCss(mode));

    [Theory]
    [InlineData(100, "")]
    [InlineData(150, "")]
    [InlineData(90, "opacity:0.9;")]
    [InlineData(5, "opacity:0.05;")]
    [InlineData(0, "opacity:0;")]
    [InlineData(-5, "opacity:0;")]
    public void OpacityCss_EmitsDeclarationOnlyBelowFull(int pct, string expected) =>
        Assert.Equal(expected, LayerRenderMode.OpacityCss(pct));

    [Theory]
    [InlineData("/a/b.mp4", true)]
    [InlineData("/a/b.MOV", true)]
    [InlineData("/a/b.webm", true)]
    [InlineData("/a/b.m4v", true)]
    [InlineData("/a/b.png", false)]
    [InlineData("/a/b.html", false)]
    [InlineData(null, false)]
    public void IsVideo_DetectsByExtension(string? path, bool expected) =>
        Assert.Equal(expected, LayerRenderMode.IsVideo(path));
}
