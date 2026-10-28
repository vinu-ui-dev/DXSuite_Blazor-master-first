namespace DXSuite.Director.Models
{
    /// <summary>
    /// Neutral input for ContentPreviewPlayer, so schedule ContentItems and playlist MediaFiles
    /// (plus not-yet-submitted uploads) can all feed the same rotation preview.
    /// </summary>
    public class PreviewItem
    {
        public string Url { get; set; } = "";

        /// <summary>Types.AssetType value driving per-type rendering.</summary>
        public int AssetTypeId { get; set; }

        /// <summary>
        /// Rotation dwell time. Null lets the player decide: videos advance when playback ends,
        /// images use the player's default image duration.
        /// </summary>
        public double? DurationSeconds { get; set; }

        /// <summary>Optional corner badge, e.g. "Not yet submitted" or "Pending approval".</summary>
        public string? Badge { get; set; }
    }
}
