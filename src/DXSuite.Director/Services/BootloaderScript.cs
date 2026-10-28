using System.IO.Compression;
using System.Security.Cryptography;

namespace DXSuite.Director.Services;

/// <summary>
/// Pulls the autorun out of a bootloader artifact.
///
/// A bootloader artifact is a zip, because that is what the app's own update path consumes: it
/// downloads the zip and the running bootloader unpacks it. The recovery courier cannot do that.
/// It runs from tmp: on a player with nothing working, and its whole safety property is that it
/// commits by a single rename, so handing it an archive to unpack would trade the atomic commit
/// for unpack logic in the one script that has to survive everything.
///
/// So the recovery route serves the script itself and the courier stays a downloader. The cost is
/// that the hash the courier verifies cannot be the artifact's recorded ZipFileHash, which covers
/// the zip; it has to be computed over the same bytes the player receives.
/// </summary>
public static class BootloaderScript
{
    /// <summary>The autorun must sit at the root of the artifact, which is what release.yml verifies.</summary>
    public const string EntryName = "autorun.brs";

    /// <summary>The bytes a player is served, or null if the artifact carries no root autorun.</summary>
    public static byte[]? Extract(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(EntryName);
            if (entry == null)
                return null;

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static string Sha256Hex(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
