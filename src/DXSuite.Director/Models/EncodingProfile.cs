namespace DXSuite.Director.Models
{
    public class EncodingProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int SortOrder { get; set; }

        // Resolution condition: which incoming videos this profile applies to
        // "all" = all resolutions | "max" = width or height exceeds Max | "custom" = exact match
        public string ResolutionMode { get; set; } = "all";
        public int? MaxWidth { get; set; }
        public int? MaxHeight { get; set; }
        public int? CustomWidth { get; set; }
        public int? CustomHeight { get; set; }

        // Re-encode targets — null means no constraint on that dimension
        public int? MaxBitrateKbps { get; set; }
        public int? MaxFps { get; set; }
        public string Codec { get; set; } = "h264"; // "h264" | "h265" | "auto"
        public bool StripAudio { get; set; } = false;
    }
}
