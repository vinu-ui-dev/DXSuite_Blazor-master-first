namespace DXSuite.Director.Models;

/// <summary>
/// One choice in a content filter list. A null <see cref="Key"/> is Shared content: assets holding no value
/// for any of the location's participating keys.
/// </summary>
public sealed record ContentFilterOption(string? Key, string? Value)
{
    public static ContentFilterOption Shared { get; } = new(null, null);

    public string Label => Key == null ? "Shared" : $"{Key}: {Value}";
}
