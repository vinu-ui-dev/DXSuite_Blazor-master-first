namespace DXSuite.Shared.Module;

/// <summary>
/// A destination a module offers as a user's landing page, beyond its nav items.
/// <paramref name="Group"/> heads the option group in the picker so these stay visually
/// separate from module navigation.
/// </summary>
public record LandingPageOption(string Label, string Href, string Group);
