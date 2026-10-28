using DXSuite.Director.Models;
using DXSuite.Director.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

public class MatchProfileTests
{
    private static VideoProcessingService CreateService() =>
        new(Options.Create(new FfmpegOptions()),
            new FakeWebHostEnvironment(),
            NullLogger<VideoProcessingService>.Instance);

    // Values probed from UOT_Abroad2026_ProReHQ_APPROVED_v2.mov (Apple ProRes 422 HQ master):
    // 1920x1080, ~157 Mbps video (~159 Mbps total incl. PCM audio), 23.976 fps, has audio.
    private static VideoAnalysisResult ProResMaster() => new()
    {
        Width = 1920,
        Height = 1080,
        TotalBitrateKbps = 158_915,
        VideoBitrateKbps = 157_379,
        HasAudioTrack = true,
        Codec = "prores",
        Fps = 24,
        Duration = 147.23,
    };

    [Fact]
    public void ProResMaster_TrippedByBitrateCeiling()
    {
        var svc = CreateService();
        // A typical signage bitrate cap (20 Mbps). The 157 Mbps master blows past it.
        var profiles = new[] { new EncodingProfile { ResolutionMode = "all", MaxBitrateKbps = 20_000 } };

        Assert.NotNull(svc.MatchProfile(ProResMaster(), profiles));
    }

    [Fact]
    public void NonDeliveryCodec_TrippedEvenWhenUnderEveryThreshold()
    {
        var svc = CreateService();
        // A ProRes source that happens to sit under the bitrate ceiling: bitrate/fps/resolution
        // checks alone would miss it, but the player still cannot decode ProRes. The codec
        // trigger is what catches it — this is the regression guard for the new behavior.
        var lowBitrateProRes = ProResMaster();
        lowBitrateProRes.VideoBitrateKbps = 8_000;
        lowBitrateProRes.TotalBitrateKbps = 9_000;
        var profiles = new[] { new EncodingProfile { ResolutionMode = "all", MaxBitrateKbps = 20_000 } };

        Assert.NotNull(svc.MatchProfile(lowBitrateProRes, profiles));
    }

    [Fact]
    public void CompliantH264_UnderThresholds_NeedsNoReencode()
    {
        var svc = CreateService();
        // A well-formed delivery file must NOT be flagged — guards against false positives
        // from the codec trigger.
        var h264 = new VideoAnalysisResult
        {
            Width = 1920,
            Height = 1080,
            TotalBitrateKbps = 9_000,
            VideoBitrateKbps = 8_800,
            HasAudioTrack = true,
            Codec = "h264",
            Fps = 30,
            Duration = 30,
        };
        var profiles = new[] { new EncodingProfile { ResolutionMode = "all", MaxBitrateKbps = 20_000 } };

        Assert.Null(svc.MatchProfile(h264, profiles));
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DXSuite.Director.Tests";
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
