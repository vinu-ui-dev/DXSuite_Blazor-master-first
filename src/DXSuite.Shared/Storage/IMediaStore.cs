namespace DXSuite.Shared.Storage;

/// <summary>
/// Abstraction over tenant media file storage. The initial implementation uses the local
/// disk (tenant subfolder); swap to AzureBlobMediaStore without changing consumers.
/// All paths are relative to the tenant root — callers never construct absolute paths.
/// </summary>
public interface IMediaStore
{
    /// <summary>
    /// Opens a read stream for the given file. Returns null if the file does not exist.
    /// Callers are responsible for disposing the returned stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(string tenantId, string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Writes the content stream to the given path, creating intermediate directories as needed.
    /// Overwrites if a file already exists at that path.
    /// </summary>
    Task SaveAsync(string tenantId, string relativePath, Stream content, CancellationToken ct = default);

    /// <summary>Deletes the file at the given path. No-op if the file does not exist.</summary>
    Task DeleteAsync(string tenantId, string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Lists relative file paths under the given prefix (empty = tenant root).
    /// Returns paths using forward slashes regardless of the underlying OS.
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(string tenantId, string prefix = "", CancellationToken ct = default);

    /// <summary>
    /// Returns the absolute local filesystem path for a file, or null for cloud-only stores.
    /// Use this only for interop with APIs that require a physical path (e.g. FileSystemWatcher).
    /// </summary>
    string? GetLocalPath(string tenantId, string relativePath);
}
