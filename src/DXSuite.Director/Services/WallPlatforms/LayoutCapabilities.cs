namespace DXSuite.Director.Services.WallPlatforms;

public class LayoutCapabilities
{
    public bool SupportsWebBackground { get; init; }
    public bool SupportsDynamicClipSwap { get; init; }
    public int MaxZonesPerScreen { get; init; } = 1;
}
