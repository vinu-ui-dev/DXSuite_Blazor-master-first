using DXSuite.Director.Models;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

/// <summary>
/// Shapes playlist MediaFiles from their sidecars into ContentPreviewPlayer items for the uploader's preview.
/// It reads the sidecar store, not the database the manifest path reads, so it is not a delivery answer.
/// </summary>
public static class PlaylistPreviewBuilder
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
    };

    public static string? MetadataValue(MediaFile file, string fieldName) =>
        file.Metadata.FirstOrDefault(m => string.Equals(m.Name, fieldName, StringComparison.OrdinalIgnoreCase))?.Value;

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Videos always play full length (a sidecar Duration applies to image dwell only, matching
    /// player behavior); images use their sidecar Duration else the player's default.
    /// </summary>
    public static PreviewItem ToPreviewItem(MediaFile file, string? badge = null)
    {
        var isVideo = IsVideo(file.DiskPath ?? file.WebPath);
        double? duration = null;
        if (!isVideo && double.TryParse(MetadataValue(file, "Duration"), out var d) && d > 0)
            duration = d;

        return new PreviewItem
        {
            Url = file.WebPath,
            AssetTypeId = isVideo ? (int)AssetType.Video : (int)AssetType.Image,
            DurationSeconds = duration,
            Badge = badge
        };
    }
}
