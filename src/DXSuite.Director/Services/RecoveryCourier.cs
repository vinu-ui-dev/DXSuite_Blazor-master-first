using System.Text;

namespace DXSuite.Director.Services;

/// <summary>What the recovery handler decided to send, and how soon to be asked again.</summary>
/// <param name="Script">The BrightScript courier, or null for a 204.</param>
/// <param name="RetryAfter">
/// Honoured exactly by the player, in last-resort as well as on the periodic poll, so a long value
/// on a path a broken player can reach would park it broken for that long.
/// </param>
/// <param name="Reason">For the log. Never sent: nothing tells a prober which branch it hit.</param>
public sealed record RecoveryDecision(string? Script, TimeSpan? RetryAfter, string Reason)
{
    public bool HasBody => Script != null;
}

/// <summary>
/// Builds the BrightScript the recovery handler serves.
///
/// The script is a <b>courier</b> and never the payload. The OS does not write the served body to
/// the card: every request names a <c>tmp:</c> destination, and a broken autorun was found
/// untouched at its exact byte count from inside the recovery context. A handler that served the
/// real bootloader as its body would rescue nothing, because the player would run it once from
/// tmp: and boot straight back into the broken card. So the courier's first job is to fetch the
/// artifact, write the card and reboot.
/// </summary>
public static class RecoveryCourier
{
    /// <summary>
    /// Replaces a broken <c>autorun.brs</c> with the tenant's blessed known-good bootloader.
    ///
    /// A single-file replacement that leaves the A/B slots alone: a broken bootloader damages
    /// nothing else, since config.json is at the card root and the slots live under sd:/app/.
    /// </summary>
    public static string BuildRecoveryCourier(string bootloaderUrl, string? expectedSha256)
        => Build(
            title: "Engage recovery courier",
            fetches: [new Fetch(bootloaderUrl, "sd:/autorun.brs", expectedSha256, DeviceToken: null)],
            extraLines: []);

    /// <summary>
    /// Installs the flavour onto a player that has not converted yet, carrying the one secret any
    /// recovery response ever carries: a short-lived, single-use pairing grant minted for this
    /// serial at this moment. A bootstrap sitting on a card is worthless on its own.
    ///
    /// <para>
    /// <b>It fetches two artifacts, and it has to.</b> A bootloader alone reboots the player into a
    /// card with nothing in either slot, where it parks: reachable, idle, and with no widget to
    /// consume the grant it was just handed. The bundle lands at <c>sd:/a.zip</c>, the card-root
    /// path <c>InstallSlot</c> accepts when <c>sd:/staging/</c> does not exist yet, and <c>a</c>
    /// because a virgin registry selects that slot.
    /// </para>
    /// <para>
    /// <b>The bundle is fetched with the grant, not anonymously.</b> The grant is an ordinary
    /// device token, so the authenticated artifact route serves it, and no second anonymous path to
    /// tenant bundles has to exist. That matters: the bootloader's anonymous route is safe only
    /// because a bootloader is tenant-neutral by construction, and an app bundle is not something
    /// that invariant covers.
    /// </para>
    /// </summary>
    public static string BuildInstallBootstrap(
        string bootloaderUrl, string? bootloaderSha256,
        string appBundleUrl, string? appBundleSha256,
        string endpointUrl, string pairingGrantToken)
        => Build(
            title: "Engage install bootstrap",
            // The bundle is committed first and autorun.brs last, because autorun.brs is the file
            // that makes this card ours on the next boot. A power cut between the two moves then
            // leaves a card that boots as it did before rather than one that boots into a parked
            // player.
            fetches:
            [
                new Fetch(appBundleUrl, "sd:/a.zip", appBundleSha256, DeviceToken: pairingGrantToken),
                new Fetch(bootloaderUrl, "sd:/autorun.brs", bootloaderSha256, DeviceToken: null),
            ],
            extraLines:
            [
                "    ' The grant is minted fresh per response and dies with its expiry.",
                "    '",
                "    ' Built with Chr(34) rather than written as a literal, because BrightScript has",
                "    ' NO escape for a quote inside a string: a doubled quote ends one string and",
                "    ' starts another, which is a syntax error, and a script that does not compile is",
                "    ' never run at all. This line was the reason the install bootstrap failed to load",
                "    ' on every attempt, reported by the player as",
                "    '   ScriptLoadError: Syntax Error. (compile error &h02) in TMP:/recovery1.brs",
                "    q = Chr(34)",
                $"    WriteAsciiFile(\"sd:/engage-bootstrap.json\", \"{{\" + q + \"endpoint\" + q + \":\" + q + \"{Escape(endpointUrl)}\" + q + \",\" + q + \"grant\" + q + \":\" + q + \"{Escape(pairingGrantToken)}\" + q + \"}}\")",
            ]);

    /// <summary>One artifact to fetch, verify, and commit only once every fetch has succeeded.</summary>
    /// <param name="DeviceToken">
    /// Sent as an X-API-KEY header when present. Null for anything on the anonymous bootloader
    /// route, which is the path that has to work on a player with no token at all.
    /// </param>
    private sealed record Fetch(string Url, string Destination, string? ExpectedSha256, string? DeviceToken);

    /// <summary>
    /// Emits fetch-and-verify for every artifact first, and commits them all at the end.
    ///
    /// Nothing reaches the card until every download has arrived and every recorded hash has
    /// matched, so a courier that fails partway leaves the player exactly as it found it and the
    /// next scheduled GET simply tries again. That property is what makes it safe to run this on a
    /// player where everything else is already broken.
    /// </summary>
    private static string Build(string title, Fetch[] fetches, string[] extraLines)
    {
        var script = new StringBuilder();

        script.AppendLine($"' {title}");
        script.AppendLine("' Served inline by DXSuite and run from tmp:. Writes the card, then reboots.");
        script.AppendLine("Sub Main()");

        for (var i = 0; i < fetches.Length; i++)
            AppendFetch(script, fetches[i], i + 1);

        foreach (var line in extraLines)
            script.AppendLine(line);

        if (extraLines.Length > 0)
            script.AppendLine();

        for (var i = 0; i < fetches.Length; i++)
            script.AppendLine($"    MoveFile(tmp{i + 1}, \"{Escape(fetches[i].Destination)}\")");

        script.AppendLine("    RebootSystem()");
        script.AppendLine("End Sub");

        return script.ToString();
    }

    private static void AppendFetch(StringBuilder script, Fetch fetch, int n)
    {
        // Staged on the CARD, not in tmp:. MoveFile is a rename and cannot cross volumes, and tmp:
        // is a RAM disk while the destination is the SD card, so a download parked in tmp: could
        // never be committed: on the bench the courier fetched both artifacts, wrote
        // engage-bootstrap.json, moved nothing, rebooted, and did it again on the next boot.
        // Downloading straight to the card was measured working, and a rename within sd: is what
        // the bootloader itself relies on.
        script.AppendLine($"    tmp{n} = \"sd:/engage-download-{n}.tmp\"");
        script.AppendLine($"    url{n} = \"{Escape(fetch.Url)}\"");
        script.AppendLine();
        script.AppendLine($"    xfer{n} = CreateObject(\"roUrlTransfer\")");
        script.AppendLine($"    xfer{n}.SetUrl(url{n})");
        script.AppendLine($"    xfer{n}.AddHeader(\"Connection\", \"close\")");

        // X-API-KEY, not Authorization: Bearer. DeviceTokenAuthHandler reads a Bearer token only on
        // /hubs paths, so a grant presented that way is never seen and the artifact route answers
        // 401. Measured on the bench, where every update call the player made failed this way.
        if (!string.IsNullOrWhiteSpace(fetch.DeviceToken))
            script.AppendLine($"    xfer{n}.AddHeader(\"X-API-KEY\", \"{Escape(fetch.DeviceToken)}\")");

        script.AppendLine($"    ok{n} = xfer{n}.GetToFile(tmp{n})");
        script.AppendLine();
        script.AppendLine($"    if ok{n} <> 200 then");
        script.AppendLine("        ' Nothing written, nothing broken. The next scheduled GET tries again.");
        script.AppendLine("        return");
        script.AppendLine("    end if");
        script.AppendLine();

        if (!string.IsNullOrWhiteSpace(fetch.ExpectedSha256))
        {
            script.AppendLine("    ' Verified before it is allowed anywhere near the card: this runs when");
            script.AppendLine("    ' everything else on the player is already broken.");
            script.AppendLine("    '");
            script.AppendLine("    ' The handle is type-checked because roEVPDigest DOES NOT EXIST on every player:");
            script.AppendLine("    ' an HD1025 on BOS 9.1.151 returns no such object, measured. BrightScript has no");
            script.AppendLine("    ' exception handling, so calling Setup() on that would fault the one script that");
            script.AppendLine("    ' runs when nothing else works. Absent, verification is skipped rather than fatal,");
            script.AppendLine("    ' which is the same call the bootloader's own StagedDigestOk makes: the artifact");
            script.AppendLine("    ' still arrived over TLS from DXSuite, and no recovery at all is strictly worse.");
            script.AppendLine($"    digest{n} = CreateObject(\"roEVPDigest\")");
            script.AppendLine($"    if type(digest{n}) = \"roEVPDigest\" then");
            script.AppendLine($"        digest{n}.Setup(\"sha256\")");
            script.AppendLine($"        if LCase(digest{n}.ProcessFile(tmp{n})) <> \"{Escape(fetch.ExpectedSha256.ToLowerInvariant())}\" then");
            script.AppendLine($"            DeleteFile(tmp{n})");
            script.AppendLine("            return");
            script.AppendLine("        end if");
            script.AppendLine("    end if");
            script.AppendLine();
        }
    }

    /// <summary>
    /// Makes a value safe to sit inside a BrightScript string literal.
    ///
    /// It REMOVES quotes rather than doubling them. BrightScript has no escape for a quote inside a
    /// string, so there is nothing to double into: a second quote simply ends the literal. Doubling
    /// was the previous behaviour and it produced a script that could not compile, which the player
    /// reports as a load error and never executes.
    ///
    /// Removing is safe for everything this emits. A URL and a device token have no legitimate
    /// quote in them, so a value carrying one is either a mistake or an attempt to break out, and
    /// neither deserves to survive into the script.
    /// </summary>
    private static string Escape(string value)
        => value.Replace("\"", string.Empty);
}
