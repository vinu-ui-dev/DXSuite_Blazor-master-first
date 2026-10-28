namespace DXSuite.Director.Models;

/// <summary>
/// Render Mode semantics for airline background/mask layer media, shared by the admin
/// preview surfaces so they match the wall shell's rendering:
///   fill-width  — scale to the region width, aspect preserved, anchored top-left
///   fill-height — scale to the region height, aspect preserved, anchored top-left
///   fit         — contain within the region, centered
///   tile        — images repeat at native size; videos fall back to native until
///                 spatial video tiling exists (multi-video layer support)
///   native / unset — pixel-exact: natural size anchored top-left, overflow clipped
/// </summary>
public static class LayerRenderMode
{
    /// <summary>CSS for image content rendered as a background-image div.</summary>
    public static string ImageCss(string? path, string? mode)
    {
        var size = mode switch
        {
            "fill-width"  => "background-size:100% auto; background-position:top left; background-repeat:no-repeat;",
            "fill-height" => "background-size:auto 100%; background-position:top left; background-repeat:no-repeat;",
            "fit"         => "background-size:contain; background-position:center; background-repeat:no-repeat;",
            "tile"        => "background-size:auto; background-position:top left; background-repeat:repeat;",
            _             => "background-size:auto; background-position:top left; background-repeat:no-repeat;",
        };
        return $"background-image:url('{path}'); {size}";
    }

    /// <summary>
    /// CSS appended to a video element whose base style already sizes it to the region
    /// box — later declarations override the box dimensions where the mode requires
    /// natural-aspect sizing, and the surrounding container clips any overflow.
    /// </summary>
    public static string VideoCss(string? mode) => mode switch
    {
        "fill-width"  => "height:auto;",
        "fill-height" => "width:auto;",
        "fit"         => "object-fit:contain;",
        _             => "width:auto; height:auto;",
    };

    /// <summary>
    /// CSS opacity declaration for a layer's 0–100 percentage; empty at full opacity
    /// so unchanged layers keep their existing inline styles byte-for-byte.
    /// </summary>
    public static string OpacityCss(int pct) =>
        pct >= 100 ? "" :
        $"opacity:{(Math.Max(0, pct) / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture)};";

    /// <summary>Matches the wall shell's video-extension detection.</summary>
    public static bool IsVideo(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (path.EndsWith(".mp4",  StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".mov",  StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".m4v",  StringComparison.OrdinalIgnoreCase));
}
