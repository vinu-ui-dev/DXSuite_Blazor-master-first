namespace DXSuite.Director.Entities;

/// <summary>
/// The two artifact kinds a tblAppVersion row can hold. App bundles and bootloaders share the
/// one EngageBrightSign platform row and are never offered to a device in the same response.
/// </summary>
public static class ArtifactKinds
{
    public const string App = "App";
    public const string Bootloader = "Bootloader";

    public static bool IsValid(string? value)
        => value is App or Bootloader;
}
