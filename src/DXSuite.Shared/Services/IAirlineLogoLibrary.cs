namespace DXSuite.Shared.Services;

/// <summary>The Director asset library airline logos are drawn from. Width and Height are its resolution constraint.</summary>
public sealed record AirlineLogoLibraryInfo(string Name, int Width, int Height);

/// <summary>A logo file in the library. Approved is false while it waits on the library's approval chain.</summary>
public sealed record AirlineLogoFile(int AssetId, string Name, string Path, bool Approved);

/// <summary>Where a library file stands on the library's approval chain.</summary>
public enum LogoApprovalState { Pending, Approved, Declined, Gone }

/// <summary>
/// Cross-module contract so the Aviation module can pick and upload airline logos without referencing Director.
/// Implemented by Director; absent when that module is not installed.
/// </summary>
public interface IAirlineLogoLibrary
{
    /// <summary>Null when no matching asset library exists yet.</summary>
    Task<AirlineLogoLibraryInfo?> GetLibraryAsync(CancellationToken ct = default);

    /// <summary>The library's approved, non-deleted files.</summary>
    Task<IReadOnlyList<AirlineLogoFile>> ListApprovedAsync(CancellationToken ct = default);

    /// <summary>
    /// The approval state of each of <paramref name="assetIds"/>. An id the library no longer holds (deleted) is
    /// <see cref="LogoApprovalState.Gone"/>. A file with no approval row predates the chain and counts as approved.
    /// </summary>
    Task<IReadOnlyDictionary<int, LogoApprovalState>> GetApprovalStatesAsync(IReadOnlyCollection<int> assetIds, CancellationToken ct = default);

    /// <summary>
    /// Director's own upload dialog for the library (resolution check, "uploading as", approval chain). Render it
    /// with DynamicComponent and an <c>OnUploaded</c> EventCallback of <see cref="AirlineLogoFile"/>; the rendered
    /// instance implements <see cref="IAirlineLogoUploader"/>.
    /// </summary>
    Type UploaderComponentType { get; }
}

public interface IAirlineLogoUploader
{
    /// <summary>Opens the upload dialog. Returns an error message when it cannot, otherwise null.</summary>
    Task<string?> OpenAsync();
}
