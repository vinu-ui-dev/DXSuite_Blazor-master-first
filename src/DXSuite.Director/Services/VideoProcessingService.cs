using System.Text.Json;
using DXSuite.Director.Models;
using FFMpegCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services
{
    public class VideoProcessingService : IVideoProcessingService
    {
        private readonly string _ffmpegPath;
        private readonly ILogger<VideoProcessingService> _logger;

        // Codecs a signage player decodes directly. Anything else (ProRes, DNxHD, MJPEG,
        // uncompressed, …) is a mastering/intermediate format that must be re-encoded for
        // delivery regardless of its bitrate. Matched case-insensitively against ffprobe's
        // codec_name, which reports "h264" / "hevc" for the delivery formats.
        private static readonly HashSet<string> DeliveryCodecs =
            new(StringComparer.OrdinalIgnoreCase) { "h264", "hevc", "h265" };

        public VideoProcessingService(IOptions<FfmpegOptions> options, IWebHostEnvironment env, ILogger<VideoProcessingService> logger)
        {
            // Resolve to an absolute folder; a relative value would bind against the process
            // working directory (which differs under IIS) and break FFMpegCore's binary lookup.
            _ffmpegPath = options.Value.ResolveFolder(env.ContentRootPath);
            _logger = logger;
        }

        public async Task<VideoAnalysisResult?> AnalyzeAsync(string filePath)
        {
            try
            {
                GlobalFFOptions.Configure(new FFOptions
                {
                    BinaryFolder = _ffmpegPath,
                    TemporaryFilesFolder = Path.GetTempPath()
                });

                var info = await FFProbe.AnalyseAsync(filePath);
                if (info == null) return null;

                var video = info.PrimaryVideoStream;
                if (video == null) return null;

                var audio = info.PrimaryAudioStream;

                // FFProbe reports total format bitrate; subtract audio to isolate video
                int totalKbps = (int)(info.Format.BitRate / 1000);
                int audioKbps = audio != null ? (int)(audio.BitRate / 1000) : 0;
                int videoKbps = Math.Max(0, totalKbps - audioKbps);

                return new VideoAnalysisResult
                {
                    Width = video.Width,
                    Height = video.Height,
                    TotalBitrateKbps = totalKbps,
                    VideoBitrateKbps = videoKbps,
                    HasAudioTrack = audio != null,
                    Codec = video.CodecName,
                    Fps = (int)Math.Round(video.FrameRate),
                    Duration = info.Format.Duration.TotalSeconds
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Video analysis failed for {FilePath}", filePath);
                return null;
            }
        }

        public async Task<bool> ReencodeAsync(string inputPath, string outputPath,
            EncodingProfile profile, IProgress<int>? progress = null)
        {
            try
            {
                GlobalFFOptions.Configure(new FFOptions
                {
                    BinaryFolder = _ffmpegPath,
                    TemporaryFilesFolder = Path.GetTempPath()
                });

                // Determine codec
                string videoCodec = profile.Codec switch
                {
                    "h265" => "libx265",
                    "auto" => "libx264",
                    _ => "libx264"  // h264 default
                };

                // Build the scale filter for "max" mode: scale to fit within max dimensions,
                // preserving aspect ratio, rounding to even pixel counts (required by H.264/H.265)
                string? scaleFilter = null;
                if (profile.ResolutionMode == "max" &&
                    (profile.MaxWidth.HasValue || profile.MaxHeight.HasValue))
                {
                    int maxW = profile.MaxWidth ?? 99999;
                    int maxH = profile.MaxHeight ?? 99999;
                    scaleFilter =
                        $"scale=w='min({maxW}\\,iw)':h='min({maxH}\\,ih)':force_original_aspect_ratio=decrease," +
                        $"scale=trunc(iw/2)*2:trunc(ih/2)*2";
                }

                var args = FFMpegArguments
                    .FromFileInput(inputPath)
                    .OutputToFile(outputPath, overwrite: true, opt =>
                    {
                        opt.WithVideoCodec(videoCodec);
                        opt.WithCustomArgument("-crf 22");

                        if (scaleFilter != null)
                            opt.WithCustomArgument($"-vf \"{scaleFilter}\"");

                        if (profile.MaxBitrateKbps.HasValue)
                            opt.WithCustomArgument(
                                $"-maxrate {profile.MaxBitrateKbps}k -bufsize {profile.MaxBitrateKbps * 2}k");

                        if (profile.MaxFps.HasValue)
                            opt.WithFramerate(profile.MaxFps.Value);

                        opt.WithCustomArgument("-preset medium -movflags +faststart -pix_fmt yuv420p");

                        if (profile.StripAudio)
                            opt.DisableChannel(FFMpegCore.Enums.Channel.Audio);
                        else
                            opt.WithAudioCodec("aac").WithCustomArgument("-b:a 128k");
                    });

                if (progress != null)
                {
                    // NotifyOnProgress passes the current completion percentage (0–100) as a double
                    args = args.NotifyOnProgress(pct =>
                    {
                        progress.Report((int)Math.Min(99, pct));
                    }, TimeSpan.FromSeconds(1));
                }

                await args.ProcessAsynchronously();

                // FFMpegCore can resolve without throwing even when FFmpeg produces no output.
                // Verify the output file exists and is non-empty before declaring success.
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                {
                    _logger.LogError("Re-encode produced no output at {Output}", outputPath);
                    return false;
                }

                progress?.Report(100);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Re-encode failed: {Input} → {Output}", inputPath, outputPath);
                return false;
            }
        }

        public EncodingProfile? MatchProfile(VideoAnalysisResult analysis,
            IEnumerable<EncodingProfile> profiles)
        {
            foreach (var profile in profiles.OrderBy(p => p.SortOrder))
            {
                bool resolutionApplies;
                bool resolutionExceeded = false;

                switch (profile.ResolutionMode)
                {
                    case "max":
                        // "max" profiles apply to all videos — they define upper bounds, not a filter.
                        // resolutionExceeded is set independently to flag whether a downscale is needed.
                        resolutionApplies = true;
                        resolutionExceeded =
                            (profile.MaxWidth.HasValue && analysis.Width > profile.MaxWidth.Value) ||
                            (profile.MaxHeight.HasValue && analysis.Height > profile.MaxHeight.Value);
                        break;
                    case "custom":
                        resolutionApplies =
                            profile.CustomWidth.HasValue && profile.CustomHeight.HasValue &&
                            analysis.Width == profile.CustomWidth.Value &&
                            analysis.Height == profile.CustomHeight.Value;
                        break;
                    default: // "all"
                        resolutionApplies = true;
                        break;
                }

                if (!resolutionApplies) continue;

                bool bitrateExceeded = profile.MaxBitrateKbps.HasValue &&
                                       analysis.VideoBitrateKbps > profile.MaxBitrateKbps.Value;
                bool fpsExceeded = profile.MaxFps.HasValue &&
                                   analysis.Fps > profile.MaxFps.Value;
                // Stripping audio requires a re-encode only when the source actually has a track
                bool audioNeedsChange = profile.StripAudio && analysis.HasAudioTrack;
                // A non-delivery source codec (e.g. ProRes) must be re-encoded even when it sits
                // under every bitrate/fps/resolution bound — the player cannot decode it at all.
                bool codecNeedsChange = !string.IsNullOrEmpty(analysis.Codec) &&
                                        !DeliveryCodecs.Contains(analysis.Codec);

                if (bitrateExceeded || fpsExceeded || resolutionExceeded || audioNeedsChange || codecNeedsChange)
                    return profile;
            }

            return null;
        }
    }
}
