using FFMpegCore;
using SkiaSharp;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// Generates normalized preview thumbnails for video and image assets, written to a Thumbnails
    /// subfolder beside the source file as {name}.png — the convention card components resolve against.
    /// Output preserves the source aspect ratio, is downscaled (never upscaled) to a bounded size, and
    /// is composited onto a checkerboard backdrop so transparent content stays legible in the UI.
    /// </summary>
    public class ThumbnailService
    {
        private const int MaxDimension = 480;
        private const int CheckerCellSize = 12;
        // 64 MP (~256 MB decoded RGBA). Decode allocates the full pixel buffer up front, so a
        // small file declaring enormous bounds (e.g. a remote og:image) could exhaust memory.
        private const long MaxDecodePixels = 64_000_000;
        private static readonly SKColor CheckerLight = new(0xFF, 0xFF, 0xFF);
        private static readonly SKColor CheckerDark = new(0xCC, 0xCC, 0xCC);

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
        };

        private readonly ILogger<ThumbnailService> _logger;

        public ThumbnailService(ILogger<ThumbnailService> logger)
        {
            _logger = logger;
        }

        public static bool SupportsThumbnail(string mediaPath)
        {
            var extension = Path.GetExtension(mediaPath);
            return VideoExtensions.Contains(extension) || ImageExtensions.Contains(extension);
        }

        public static string GetThumbnailPath(string mediaPath)
        {
            var directory = Path.Combine(Path.GetDirectoryName(mediaPath)!, "Thumbnails");
            return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(mediaPath)}.png");
        }

        /// <summary>
        /// Removes the thumbnail belonging to a media file, if one was ever written. Returns true only
        /// when a file was actually deleted. Thumbnails are a regenerable cache, so a failure is logged
        /// and swallowed rather than failing whatever operation prompted the cleanup.
        /// </summary>
        public bool DeleteThumbnail(string mediaPath)
        {
            try
            {
                var thumbnailPath = GetThumbnailPath(mediaPath);
                if (!File.Exists(thumbnailPath)) return false;

                File.Delete(thumbnailPath);
                _logger.LogInformation("Deleted thumbnail {ThumbnailPath}", thumbnailPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not delete the thumbnail for {MediaPath}", mediaPath);
                return false;
            }
        }

        /// <summary>
        /// Generates a thumbnail for the given media file. Unsupported file types return null without error
        /// so callers can invoke this unconditionally after an upload.
        /// </summary>
        public async Task<string?> GenerateThumbnailAsync(string mediaPath)
        {
            if (!File.Exists(mediaPath)) return null;

            var extension = Path.GetExtension(mediaPath);
            if (VideoExtensions.Contains(extension)) return await GenerateVideoThumbnailAsync(mediaPath);
            if (ImageExtensions.Contains(extension)) return GenerateImageThumbnail(mediaPath);
            return null;
        }

        private async Task<string?> GenerateVideoThumbnailAsync(string videoPath)
        {
            // FFMpegCore binaries resolve via the process-wide GlobalFFOptions configured at
            // startup (AddDirectorModule) with the content-root-resolved folder; reconfiguring
            // here with the raw config value would clobber that for all concurrent callers.

            // Short clips can't be sampled at the default offset; probe first and capture mid-clip instead
            var captureTime = TimeSpan.FromSeconds(2);
            try
            {
                var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
                if (mediaInfo.Duration > TimeSpan.Zero && mediaInfo.Duration < TimeSpan.FromSeconds(4))
                    captureTime = mediaInfo.Duration / 2;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FFProbe analysis failed for {Path}; using default capture time", videoPath);
            }

            var tempFramePath = Path.Combine(Path.GetTempPath(), $"dx-thumb-{Guid.NewGuid():N}.png");
            try
            {
                var success = await FFMpeg.SnapshotAsync(videoPath, tempFramePath, null, captureTime);
                if (!success || !File.Exists(tempFramePath))
                {
                    _logger.LogWarning("FFmpeg snapshot failed for {Path}", videoPath);
                    return null;
                }

                return WriteNormalized(await File.ReadAllBytesAsync(tempFramePath), videoPath);
            }
            finally
            {
                if (File.Exists(tempFramePath)) File.Delete(tempFramePath);
            }
        }

        private string? GenerateImageThumbnail(string imagePath)
        {
            return WriteNormalized(File.ReadAllBytes(imagePath), imagePath);
        }

        /// <summary>
        /// Normalizes arbitrary image bytes (e.g. a web page capture or og:image download) and writes
        /// the result to an explicit output path. Returns null if the bytes can't be decoded.
        /// </summary>
        public string? GenerateFromImageBytes(byte[] sourceBytes, string outputPath)
        {
            return WriteNormalized(sourceBytes, outputPath, outputPath);
        }

        private string? WriteNormalized(byte[] sourceBytes, string mediaPath)
        {
            return WriteNormalized(sourceBytes, mediaPath, GetThumbnailPath(mediaPath));
        }

        private string? WriteNormalized(byte[] sourceBytes, string mediaPath, string thumbnailPath)
        {
            try
            {
                var bounds = SKBitmap.DecodeBounds(sourceBytes);
                if ((long)bounds.Width * bounds.Height > MaxDecodePixels)
                {
                    _logger.LogWarning("Refusing to decode {Width}x{Height} media for thumbnail: {Path}",
                        bounds.Width, bounds.Height, mediaPath);
                    return null;
                }

                using var bitmap = SKBitmap.Decode(sourceBytes);
                if (bitmap == null)
                {
                    _logger.LogWarning("Could not decode media for thumbnail: {Path}", mediaPath);
                    return null;
                }

                var longest = Math.Max(bitmap.Width, bitmap.Height);
                var scale = longest > MaxDimension ? (float)MaxDimension / longest : 1f;
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

                SKBitmap? resized = null;
                try
                {
                    var source = bitmap;
                    if (scale < 1f)
                    {
                        resized = bitmap.Resize(new SKImageInfo(width, height),
                            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                        if (resized == null)
                        {
                            _logger.LogWarning("Could not resize media for thumbnail: {Path}", mediaPath);
                            return null;
                        }
                        source = resized;
                    }

                    using var surface = SKSurface.Create(new SKImageInfo(width, height));
                    var canvas = surface.Canvas;
                    DrawCheckerboard(canvas, width, height);
                    canvas.DrawBitmap(source, 0, 0);

                    using var image = surface.Snapshot();
                    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

                    Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
                    File.WriteAllBytes(thumbnailPath, encoded.ToArray());
                    return thumbnailPath;
                }
                finally
                {
                    resized?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail normalization failed for {Path}", mediaPath);
                return null;
            }
        }

        private static void DrawCheckerboard(SKCanvas canvas, int width, int height)
        {
            canvas.Clear(CheckerLight);
            using var paint = new SKPaint { Color = CheckerDark };
            for (var row = 0; row * CheckerCellSize < height; row++)
            {
                for (var col = row % 2 == 0 ? 1 : 0; col * CheckerCellSize < width; col += 2)
                {
                    canvas.DrawRect(col * CheckerCellSize, row * CheckerCellSize, CheckerCellSize, CheckerCellSize, paint);
                }
            }
        }
    }
}
