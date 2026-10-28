using DXSuite.Director.Models;

namespace DXSuite.Director.Services
{
    public interface IVideoProcessingService
    {
        /// <summary>
        /// Probe a video file and return its technical properties.
        /// Returns null if the file cannot be analysed (not a video, ffprobe missing, etc.).
        /// </summary>
        Task<VideoAnalysisResult?> AnalyzeAsync(string filePath);

        /// <summary>
        /// Re-encode a video file according to the given encoding profile.
        /// Returns true on success, false on failure.
        /// </summary>
        Task<bool> ReencodeAsync(string inputPath, string outputPath,
            EncodingProfile profile, IProgress<int>? progress = null);

        /// <summary>
        /// Find the first profile (ordered by SortOrder) whose resolution condition applies to
        /// the video AND at least one threshold is exceeded. Returns null if no re-encode is needed.
        /// </summary>
        EncodingProfile? MatchProfile(VideoAnalysisResult analysis,
            IEnumerable<EncodingProfile> profiles);
    }

    public class VideoAnalysisResult
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int TotalBitrateKbps { get; set; }
        public int VideoBitrateKbps { get; set; }
        public bool HasAudioTrack { get; set; }
        public string? Codec { get; set; }
        public int Fps { get; set; }
        public double Duration { get; set; }
    }
}
