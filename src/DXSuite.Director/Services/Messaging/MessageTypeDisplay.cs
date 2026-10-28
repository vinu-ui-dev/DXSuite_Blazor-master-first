using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>How the Message Types list and dialog show a type's Screen Part and colours.</summary>
public static class MessageTypeDisplay
{
    public static string ScreenPartName(MessageDisplayMode part) => part switch
    {
        MessageDisplayMode.FullScreen => "Full screen",
        MessageDisplayMode.Overlay => "Overlay",
        MessageDisplayMode.Ticker => "Ticker",
        _ => ""
    };

    public static string ScreenPartHelp(MessageDisplayMode part) => part switch
    {
        MessageDisplayMode.FullScreen => "Takes the whole display. Nothing else shows.",
        MessageDisplayMode.Overlay => "A panel over the normal content, which stays visible around it.",
        MessageDisplayMode.Ticker => "A scrolling line in the band the layout keeps for it.",
        _ => "Where on the screen the Message shows."
    };

    /// <summary>
    /// The type's colours as CSS custom properties. They are per-type data, so the stylesheet reads them as variables
    /// rather than the markup carrying colour styles.
    /// </summary>
    public static string ColourStyle(string textColour, string backgroundColour) =>
        $"--mt-text: {Safe(textColour)}; --mt-back: {Safe(backgroundColour)}";

    // Only a hex value reaches the style attribute, so a stored value can never inject other CSS.
    private static string Safe(string colour) =>
        colour.Length == 7 && colour[0] == '#' && colour.Skip(1).All(Uri.IsHexDigit) ? colour : "transparent";
}
