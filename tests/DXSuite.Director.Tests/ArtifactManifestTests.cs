using System.IO.Compression;
using System.Text;
using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The manifest is the version of record, so every way of failing to read one has to be a refusal
/// with a reason rather than a fallback to something invented.
/// </summary>
public class ArtifactManifestTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("artifact-manifest").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteZip(string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
        return path;
    }

    [Fact]
    public void ReadsTheVersionFromARootManifest()
    {
        var zip = WriteZip("app.zip",
            ("manifest.json", """{"version":"2.0.1","name":"engage"}"""),
            ("index.html", "<html></html>"));

        Assert.Equal("2.0.1", ArtifactManifest.TryReadVersion(zip, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void PrefersTheShallowestManifestOverANestedOne()
    {
        // A bundle carries its own node_modules, so nested manifests are the normal case and must
        // never be mistaken for the artifact's own.
        var zip = WriteZip("app.zip",
            ("node_modules/thing/manifest.json", """{"version":"9.9.9"}"""),
            ("manifest.json", """{"version":"2.0.1"}"""));

        Assert.Equal("2.0.1", ArtifactManifest.TryReadVersion(zip, out _));
    }

    [Fact]
    public void RefusesAZipWithNoManifest()
    {
        var zip = WriteZip("app.zip", ("index.html", "<html></html>"));

        Assert.Null(ArtifactManifest.TryReadVersion(zip, out var error));
        Assert.Contains("no manifest.json", error);
    }

    [Theory]
    [InlineData("""{"name":"engage"}""")]
    [InlineData("""{"version":""}""")]
    [InlineData("""{"version":123}""")]
    public void RefusesAManifestWithNoUsableVersion(string manifest)
    {
        var zip = WriteZip("app.zip", ("manifest.json", manifest));

        Assert.Null(ArtifactManifest.TryReadVersion(zip, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RefusesUnreadableJsonWithoutThrowing()
    {
        var zip = WriteZip("app.zip", ("manifest.json", "{ not json"));

        Assert.Null(ArtifactManifest.TryReadVersion(zip, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RefusesAMissingFile()
    {
        Assert.Null(ArtifactManifest.TryReadVersion(Path.Combine(_directory, "absent.zip"), out var error));
        Assert.Contains("not found", error);
    }

    private static MemoryStream ZipStream(params (string Path, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryPath, content) in entries)
            {
                var entry = archive.CreateEntry(entryPath);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    // ── TryReadDeclaredVersion: the upload form's path ─────────────────────────────────────
    // DXME-287: the form read only config.json, so it refused every BrightSign artifact.

    [Fact]
    public void ReadsABrightSignManifestFromAStream()
    {
        using var zip = ZipStream(("manifest.json", """{"version":"2.0.6","kind":"app"}"""));

        Assert.Equal("2.0.6", ArtifactManifest.TryReadDeclaredVersion(zip, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void FallsBackToConfigJsonForALegacySignageOsApplet()
    {
        using var zip = ZipStream(("config.json", """{"version":"1.0.2"}"""));

        Assert.Equal("1.0.2", ArtifactManifest.TryReadDeclaredVersion(zip, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void PrefersTheManifestWhenAnArtifactCarriesBoth()
    {
        using var zip = ZipStream(
            ("manifest.json", """{"version":"2.0.6"}"""),
            ("config.json", """{"version":"0.0.1"}"""));

        Assert.Equal("2.0.6", ArtifactManifest.TryReadDeclaredVersion(zip, out _));
    }

    [Fact]
    public void ABrokenManifestIsARefusalRatherThanAFallThroughToConfigJson()
    {
        // Silently preferring config.json here would publish a version the artifact does not
        // declare, which is the exact divergence the manifest exists to prevent.
        using var zip = ZipStream(
            ("manifest.json", """{"name":"engage"}"""),
            ("config.json", """{"version":"0.0.1"}"""));

        Assert.Null(ArtifactManifest.TryReadDeclaredVersion(zip, out var error));
        Assert.Contains("manifest.json", error);
    }

    [Fact]
    public void RefusesAnArtifactCarryingNeitherAndNamesBoth()
    {
        using var zip = ZipStream(("index.html", "<html></html>"));

        Assert.Null(ArtifactManifest.TryReadDeclaredVersion(zip, out var error));
        Assert.Contains("manifest.json", error);
        Assert.Contains("config.json", error);
    }

    [Fact]
    public void LeavesTheStreamOpenSoTheCallerCanStoreTheArtifact()
    {
        // The upload form reads the version and then rewinds the same buffer to save the zip.
        using var zip = ZipStream(("manifest.json", """{"version":"2.0.3"}"""));

        Assert.Equal("2.0.3", ArtifactManifest.TryReadDeclaredVersion(zip, out _));

        zip.Position = 0;
        using var reopened = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        Assert.NotNull(reopened.GetEntry("manifest.json"));
    }

    [Fact]
    public void RefusesUnreadableStreamJsonWithoutThrowing()
    {
        using var zip = ZipStream(("manifest.json", "{ not json"));

        Assert.Null(ArtifactManifest.TryReadDeclaredVersion(zip, out var error));
        Assert.NotNull(error);
    }
}
