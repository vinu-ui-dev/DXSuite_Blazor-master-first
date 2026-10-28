using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DXSuite.Director.Controllers;
using DXSuite.Director.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The recovery route serves a bootloader to a courier that commits it by renaming it onto
/// sd:/autorun.brs. Serving the artifact instead of the autorun inside it put a zip at that name,
/// which the OS will not run: measured on the bench as a player that fetched 14,824 bytes, wrote
/// them to autorun.brs, rebooted, and landed back in recovery.
/// </summary>
public class BootloaderScriptTests
{
    private const string ScriptBody = "Sub Main()\n    print \"engage\"\nEnd Sub\n";

    [Fact]
    public void TheAutorunIsExtractedRatherThanTheArtifact()
    {
        var path = WriteArtifact(withAutorun: true);

        var script = BootloaderScript.Extract(path);

        Assert.NotNull(script);
        Assert.Equal(ScriptBody, Encoding.UTF8.GetString(script!));
    }

    [Fact]
    public void TheHashCoversWhatThePlayerReceivesNotTheZip()
    {
        // The recorded ZipFileHash covers the artifact. A courier handed that hash would reject
        // every bootloader that arrived perfectly intact, so the two must not be interchangeable.
        var path = WriteArtifact(withAutorun: true);

        var served = BootloaderScript.Sha256Hex(BootloaderScript.Extract(path)!);
        var artifact = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        Assert.NotEqual(artifact, served);
    }

    [Fact]
    public void AnArtifactWithNoRootAutorunExtractsNothing()
    {
        // Serving something else under the name autorun.brs is worse than serving nothing: the
        // caller can retry a 404, and cannot undo a card it has already written.
        var path = WriteArtifact(withAutorun: false);

        Assert.Null(BootloaderScript.Extract(path));
    }

    [Fact]
    public void SomethingThatIsNotAZipExtractsNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}.zip");
        File.WriteAllText(path, "not a zip");

        try
        {
            Assert.Null(BootloaderScript.Extract(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheZipAndTheScriptAreServedFromDifferentRoutes()
    {
        // They were one route, and the comment on ResolveArtifactUrl said "one URL rather than
        // two", which held only while both consumers wanted the same bytes. Repointing that one
        // route at the script for the courier's sake broke every bootloader update: the app
        // downloaded a .brs to sd:/staging/bootloader.zip, checked it against the recorded
        // ZipFileHash, and stopped. Measured on the HD1025, which sat on the offer doing nothing.
        var zip = Template(nameof(RecoveryController.GetBootloader));
        var script = Template(nameof(RecoveryController.GetBootloaderScript));

        Assert.NotEqual(zip, script);
        Assert.EndsWith("/autorun", script, StringComparison.Ordinal);
        Assert.False(zip.EndsWith("/autorun", StringComparison.Ordinal),
            "the update pipeline's route must keep serving the artifact");
    }

    private static string Template(string actionName)
    {
        var method = typeof(RecoveryController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"{actionName} is gone");

        var route = method.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException($"{actionName} carries no [HttpGet]");

        return route.Template ?? string.Empty;
    }

    /// <summary>A bootloader artifact as scripts/package.js builds it: the autorun plus a manifest.</summary>
    private static string WriteArtifact(bool withAutorun)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}.zip");

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            if (withAutorun)
                Write(archive, BootloaderScript.EntryName, ScriptBody);

            Write(archive, "manifest.json", "{\"version\":\"2.0.1\"}");
        }

        return path;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}
