namespace DXSuite.Director.Services;

/// <summary>
/// How a player rotates its output to match how its panel is mounted. Stored on tblDevice as the
/// model's name and sent to the player as the platform's own literal, the way a time zone is stored
/// as IANA and sent as a SetTimeZone argument.
///
/// Only Engage for BrightSign reads it. LG and Samsung players take their viewport from the panel.
/// </summary>
public static class DeviceOrientation
{
    public const string Landscape = "Landscape";

    /// <summary>The panel is turned clockwise, viewed from the front.</summary>
    public const string Portrait = "Portrait";

    public const string PortraitFlipped = "PortraitFlipped";
    public const string LandscapeFlipped = "LandscapeFlipped";

    public static IReadOnlyList<string> All { get; } =
        [Landscape, Portrait, PortraitFlipped, LandscapeFlipped];

    /// <summary>
    /// The roHtmlWidget transform for a stored value. An unrecognised value maps to identity, so a
    /// bad row leaves the panel upright rather than turning it on a guess.
    /// </summary>
    public static string ToWidgetTransform(string? orientation) => orientation switch
    {
        Portrait => "rot90",
        PortraitFlipped => "rot270",
        LandscapeFlipped => "rot180",
        _ => "identity",
    };
}
