using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Builds a content package bundle ZIP from a resolved selection. The bundle contains
/// <c>manifest.json</c> plus media files keyed by asset ContentPackageId.
/// </summary>
public interface IContentPackageBundleBuilder
{
    /// <summary>
    /// Builds a bundle and writes it to <paramref name="output"/>. The stream must be
    /// writable and seekable (ZipArchive requirement). Caller owns the stream.
    /// </summary>
    /// <param name="selection">User's selection of what to promote.</param>
    /// <param name="exportedByUser">UserPrincipalName of the initiating user, stamped in the manifest.</param>
    /// <param name="output">Destination stream that receives the ZIP bytes.</param>
    Task<ContentPackageBundleResult> BuildAsync(
        ContentPackageSelection selection,
        string? exportedByUser,
        Stream output,
        CancellationToken ct = default);
}

/// <summary>
/// Summary of a completed bundle build, used by callers for audit/logging and response metadata.
/// </summary>
public class ContentPackageBundleResult
{
    public int TotalRecords { get; set; }
    public int MediaFileCount { get; set; }
    public long TotalMediaBytes { get; set; }
    public string? SuggestedFileName { get; set; }
}
