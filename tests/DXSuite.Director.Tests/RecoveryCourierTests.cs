using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The served script is a courier, never the payload. The OS does not write the response body to
/// the card: it runs it once from tmp: and boots straight back into whatever the card holds. A
/// handler that served the real bootloader as its body would therefore rescue nothing.
/// </summary>
public class RecoveryCourierTests
{
    private const string BootloaderUrl = "https://dxsuite.example.com/api/Recovery/bootloader/acme/42";
    private const string BundleUrl = "https://dxsuite.example.com/api/Update/versions/7/artifact";
    private const string Sha = "ABCDEF0123456789";

    private static string InstallBootstrap()
        => RecoveryCourier.BuildInstallBootstrap(
            BootloaderUrl, Sha, BundleUrl, "FEDCBA9876543210", "https://dxsuite.example.com", "dt_grant-token");

    [Fact]
    public void TheCourierFetchesAndThenWritesTheCard()
    {
        var script = RecoveryCourier.BuildRecoveryCourier(BootloaderUrl, Sha);

        Assert.Contains("roUrlTransfer", script);
        Assert.Contains("sd:/engage-download-1.tmp", script);
        Assert.Contains("MoveFile(", script);
        Assert.Contains("sd:/autorun.brs", script);
        Assert.Contains("RebootSystem()", script);
    }

    [Fact]
    public void TheDigestIsVerifiedBeforeAnythingTouchesTheCard()
    {
        var script = RecoveryCourier.BuildRecoveryCourier(BootloaderUrl, Sha);

        var verifyAt = script.IndexOf("ProcessFile", StringComparison.Ordinal);
        var writeAt = script.IndexOf("MoveFile(", StringComparison.Ordinal);

        Assert.True(verifyAt > 0);
        Assert.True(verifyAt < writeAt, "the hash must be checked before the card is written");
        Assert.Contains(Sha.ToLowerInvariant(), script);
    }

    [Fact]
    public void AFailedDownloadWritesNothing()
    {
        var script = RecoveryCourier.BuildRecoveryCourier(BootloaderUrl, Sha);

        var guardAt = script.IndexOf("if ok1 <> 200 then", StringComparison.Ordinal);
        var writeAt = script.IndexOf("MoveFile(", StringComparison.Ordinal);

        Assert.True(guardAt > 0);
        Assert.True(guardAt < writeAt);
    }

    [Fact]
    public void AnArtifactWithNoRecordedHashStillInstalls()
    {
        // Older rows carry no hash. Refusing them would make recovery depend on a column that was
        // optional when the artifact was uploaded.
        var script = RecoveryCourier.BuildRecoveryCourier(BootloaderUrl, null);

        Assert.DoesNotContain("ProcessFile", script);
        Assert.Contains("MoveFile(", script);
    }

    [Fact]
    public void TheInstallBootstrapCarriesTheGrantAndTheEndpoint()
    {
        var script = InstallBootstrap();

        Assert.Contains("dt_grant-token", script);
        Assert.Contains("https://dxsuite.example.com", script);
        Assert.Contains("engage-bootstrap.json", script);
    }

    [Fact]
    public void TheInstallBootstrapAlsoInstallsAnAppBundle()
    {
        // A bootloader on its own reboots the player into a card with nothing in either slot,
        // where it parks with no widget to consume the grant it was just handed.
        var script = InstallBootstrap();

        Assert.Contains(BundleUrl, script);
        Assert.Contains("sd:/a.zip", script);
        Assert.Contains("sd:/autorun.brs", script);
    }

    [Fact]
    public void TheBundleIsFetchedWithTheGrantAndTheBootloaderIsNot()
    {
        // The bootloader route is anonymous because a broken player has no token; that rests on a
        // bootloader being tenant-neutral, which says nothing about a bundle. The grant is an
        // ordinary device token, so the bundle needs no second anonymous route.
        var script = InstallBootstrap();

        var bundleBlock = BlockFor(script, BundleUrl);
        var bootloaderBlock = BlockFor(script, BootloaderUrl);

        Assert.DoesNotContain("Authorization", bundleBlock);
        Assert.Contains("X-API-KEY", bundleBlock);
        Assert.Contains("dt_grant-token", bundleBlock);
        Assert.DoesNotContain("X-API-KEY", bootloaderBlock);
    }

    [Fact]
    public void NeitherArtifactTouchesTheCardUntilBothHaveArrived()
    {
        // The whole safety of a courier: one that fails partway leaves the player exactly as it
        // found it, and the next scheduled GET tries again.
        var script = InstallBootstrap();

        var firstWrite = script.IndexOf("MoveFile(", StringComparison.Ordinal);

        Assert.True(script.IndexOf("if ok1 <> 200 then", StringComparison.Ordinal) < firstWrite);
        Assert.True(script.IndexOf("if ok2 <> 200 then", StringComparison.Ordinal) < firstWrite);
        Assert.True(script.LastIndexOf("ProcessFile", StringComparison.Ordinal) < firstWrite);
    }

    [Fact]
    public void AutorunIsCommittedLast()
    {
        // autorun.brs is the file that makes the card ours on the next boot, so a power cut between
        // the two moves leaves a card that boots as it did before rather than a parked player.
        var script = InstallBootstrap();

        var bundleWrite = script.IndexOf("MoveFile(tmp1, \"sd:/a.zip\")", StringComparison.Ordinal);
        var autorunWrite = script.IndexOf("MoveFile(tmp2, \"sd:/autorun.brs\")", StringComparison.Ordinal);

        Assert.True(bundleWrite > 0);
        Assert.True(autorunWrite > bundleWrite);
    }

    /// <summary>The lines of the fetch block that names <paramref name="url"/>, up to the next fetch.</summary>
    private static string BlockFor(string script, string url)
    {
        var lines = script.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains(url, StringComparison.Ordinal));
        Assert.True(start >= 0, $"no fetch block names {url}");

        var end = Array.FindIndex(lines, start + 1, line => line.TrimStart().StartsWith("tmp", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Fact]
    public void TheRecoveryCourierCarriesNoSecret()
    {
        // The only response that ever carries one is the per-player install bootstrap.
        var script = RecoveryCourier.BuildRecoveryCourier(BootloaderUrl, Sha);

        Assert.DoesNotContain("grant", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dt_", script);
    }

    [Fact]
    public void AQuoteInAnInjectedValueCannotBreakOutOfTheString()
    {
        var script = RecoveryCourier.BuildInstallBootstrap(
            "https://example.com/\"; RebootSystem() '", null, BundleUrl, null, "https://example.com", "token");

        // BrightScript has NO escape for a quote inside a string: a doubled quote ends the literal
        // and starts another, which is a syntax error, and a script that will not compile is never
        // run at all. This test previously asserted the opposite and passed, which is how the
        // install bootstrap shipped for a day without ever loading on a player.
        //
        // So a hostile quote is removed rather than doubled, and the url line stays exactly one
        // complete literal.
        var urlLine = script.Split('\n').Single(line => line.TrimStart().StartsWith("url2 ="));

        Assert.DoesNotContain("\"\"", urlLine);
        Assert.Equal(2, urlLine.Count(c => c == '"'));
    }

    [Fact]
    public void NoEmittedLineCarriesADoubledQuote()
    {
        // The whole script, not just the url line. A doubled quote anywhere is a compile error, and
        // the JSON line carried one, which the player reported as
        // "ScriptLoadError: Syntax Error. (compile error &h02) in TMP:/recovery1.brs".
        Assert.DoesNotContain("\"\"", InstallBootstrap());
    }

    [Fact]
    public void TheBootstrapJsonIsBuiltFromChr34()
    {
        // Concatenating Chr(34) is the only way to put a quote inside a BrightScript string.
        var script = InstallBootstrap();

        Assert.Contains("q = Chr(34)", script);
        Assert.Contains("engage-bootstrap.json", script);
        Assert.Contains("+ q +", script);
    }

    [Fact]
    public void EveryEmittedLineHasBalancedQuotes()
    {
        // A cheap stand-in for the compiler nobody has off-player: an odd number of quotes on a
        // line means a literal was left open, which is exactly the defect this file now exists to
        // keep out.
        foreach (var line in InstallBootstrap().Split('\n'))
        {
            if (line.TrimStart().StartsWith("'")) continue;
            Assert.True(line.Count(c => c == '"') % 2 == 0, $"unbalanced quotes: {line}");
        }
    }

    [Fact]
    public void TheDigestHandleIsTypeCheckedBeforeItIsUsed()
    {
        // roEVPDigest does not exist on every player: an HD1025 on BOS 9.1.151 returns no such
        // object, measured on the bench. BrightScript has no exception handling, so an unguarded
        // Setup() call faults the one script that runs when nothing else on the player works.
        var script = InstallBootstrap();

        Assert.Contains("if type(digest1) = \"roEVPDigest\" then", script);

        var create = script.IndexOf("digest1 = CreateObject", StringComparison.Ordinal);
        var guard = script.IndexOf("if type(digest1)", StringComparison.Ordinal);
        var use = script.IndexOf("digest1.Setup", StringComparison.Ordinal);

        Assert.True(create < guard, "the handle is created before it is checked");
        Assert.True(guard < use, "the handle is checked before it is used");
    }

    [Fact]
    public void AMissingDigestSkipsVerificationRatherThanRefusing()
    {
        // Same call the bootloader's StagedDigestOk makes. The artifact still arrived over TLS,
        // and no recovery at all is strictly worse than recovery without the second check.
        var script = InstallBootstrap();

        var guardEnd = script.IndexOf("if type(digest1)", StringComparison.Ordinal);
        var commit = script.IndexOf("MoveFile(", StringComparison.Ordinal);

        Assert.True(guardEnd > 0 && commit > guardEnd);
        Assert.Contains("MoveFile(tmp1", script);
    }

    [Fact]
    public void ArtifactsAreStagedOnTheCardNotInTmp()
    {
        // MoveFile is a rename and cannot cross volumes. tmp: is a RAM disk and the destination is
        // the SD card, so anything downloaded to tmp: can never be committed. Measured: the courier
        // fetched both artifacts, wrote the bootstrap json, moved nothing, and rebooted into the
        // same loop.
        var script = InstallBootstrap();

        Assert.DoesNotContain("tmp:/engage-download", script);
        Assert.Contains("sd:/engage-download-1.tmp", script);
        Assert.Contains("sd:/engage-download-2.tmp", script);
    }
}
