using System.Text.RegularExpressions;

namespace DXSuite.Director.Services;

/// <summary>
/// How an Engage for BrightSign player draws video: on the hardware video plane, or in the page.
/// Stored on tblDevice as the model's name and sent to the player as its own literal, the way
/// Orientation is.
///
/// Page-drawn video cuts from one clip to the next with no dark frame; the hardware plane cannot
/// get below a few dark frames, however early the next clip is started (filmed on an HD1025,
/// DXME-289). Page-drawn is Full HD only and needs a player that decodes two clips at once, so
/// it is off unless an operator turns it on.
/// </summary>
public static partial class DeviceVideoPath
{
    public const string Hardware = "Hardware";
    public const string Page = "Page";

    public static IReadOnlyList<string> All { get; } = [Hardware, Page];

    /// <summary>
    /// Series 4 players ignore hwz="off" from BOS 9.1.151 and always play on the hardware plane, and
    /// a player in page mode keeps its page opaque, so page mode would hide their video entirely. The
    /// series is the last digit of the model number: LS424 and XT1144 are Series 4, HD1025 Series 5.
    /// An unrecognised model is allowed, because the player refuses page mode on Series 4 as well.
    /// </summary>
    public static bool SupportsPage(string? model)
    {
        var match = SeriesDigit().Match(model ?? "");
        return !match.Success || match.Groups[1].Value[0] >= '5';
    }

    /// <summary>
    /// The player's literal for a stored value. Anything but Page on a supporting model is the
    /// hardware plane, so a bad row or a Series 4 player never loses its video.
    /// </summary>
    public static string ToPlayerValue(string? videoPath, string? model) =>
        videoPath == Page && SupportsPage(model) ? "page" : "hardware";

    [GeneratedRegex(@"(\d)\D*$")]
    private static partial Regex SeriesDigit();
}
