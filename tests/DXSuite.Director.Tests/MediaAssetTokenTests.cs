using DXSuite.Director.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The token stands in front of the media root, so what matters is that it strips cleanly on the
/// way through and that every failure looks identical from outside.
/// </summary>
public class MediaAssetTokenTests
{
    private const string GoodToken = "tenant-token-value";

    private sealed class StubTokenService(string? valid) : IMediaAccessTokenService
    {
        public Task<string> GetOrCreateAsync() => Task.FromResult(valid ?? "");
        public Task<bool> IsValidAsync(string? token) => Task.FromResult(token == valid);
    }

    private static async Task<(int Status, string Path, bool Continued)> RunAsync(
        string requestPath, string? validToken, bool enforce)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DXSuite:MediaAssets:RequireToken"] = enforce.ToString()
            })
            .Build();

        var continued = false;
        var middleware = new MediaAssetTokenMiddleware(_ => { continued = true; return Task.CompletedTask; }, config);

        var context = new DefaultHttpContext();
        context.Request.Path = requestPath;

        await middleware.InvokeAsync(context, new StubTokenService(validToken));

        return (context.Response.StatusCode, context.Request.Path.Value ?? "", continued);
    }

    [Fact]
    public async Task AValidTokenIsStrippedAndTheRequestContinues()
    {
        // Everything downstream, the tenant rewrite included, must see the path it expects.
        var result = await RunAsync($"/MediaAssets/t/{GoodToken}/22/clip.mp4", GoodToken, enforce: true);

        Assert.True(result.Continued);
        Assert.Equal("/MediaAssets/22/clip.mp4", result.Path);
    }

    [Fact]
    public async Task AWrongTokenIs404AndGoesNoFurther()
    {
        var result = await RunAsync("/MediaAssets/t/not-the-token/22/clip.mp4", GoodToken, enforce: true);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    [Fact]
    public async Task AnUntokenisedPathIs404WhenEnforced()
    {
        // The same 404 as a wrong token and as a missing file: nothing tells a prober which.
        var result = await RunAsync("/MediaAssets/22/clip.mp4", GoodToken, enforce: true);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    [Fact]
    public async Task AnUntokenisedPathPassesWhileEnforcementIsOff()
    {
        // Enforcement is opt-in so the server can emit tokenised URLs for a manifest cycle before
        // refusing the URLs players already cached.
        var result = await RunAsync("/MediaAssets/22/clip.mp4", GoodToken, enforce: false);

        Assert.True(result.Continued);
        Assert.Equal("/MediaAssets/22/clip.mp4", result.Path);
    }

    [Fact]
    public async Task AWrongTokenIsRefusedEvenWhileEnforcementIsOff()
    {
        // Presenting a token is a claim. A false one is refused whatever the gate says.
        var result = await RunAsync("/MediaAssets/t/not-the-token/22/clip.mp4", GoodToken, enforce: false);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    [Fact]
    public async Task PathsOutsideTheMediaRootAreUntouched()
    {
        var result = await RunAsync("/images/logo.png", GoodToken, enforce: true);

        Assert.True(result.Continued);
        Assert.Equal("/images/logo.png", result.Path);
    }

    [Fact]
    public async Task ANestedAssetPathSurvivesTheStrip()
    {
        var result = await RunAsync($"/MediaAssets/t/{GoodToken}/22/sub/dir/clip.mp4", GoodToken, enforce: true);

        Assert.Equal("/MediaAssets/22/sub/dir/clip.mp4", result.Path);
    }
}
