using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// What a provisioning card's config.json must look like to the bootloader that reads it.
///
/// Every assertion here runs through <see cref="BootloaderReader"/>, a port of the reader in
/// autorun.brs, rather than through a JSON parser. A file that a JSON parser accepts and the player
/// misreads is exactly the failure this file exists to catch, and the player is the only reader
/// whose opinion matters.
/// </summary>
public class ProvisioningCardConfigTests
{
    /// <summary>
    /// The keys <c>ConfigKeys()</c> in autorun.brs iterates, in its order. A key this list holds and
    /// the generator does not emit reads as absent on the player, silently.
    /// </summary>
    private static readonly string[] BootloaderKeys =
    [
        "fingerprint", "endpoint", "customer_id", "api_key", "proxy", "proxy_bypass",
        "time_server", "dws", "dws_password", "recovery_url", "ca_file"
    ];

    private static ProvisioningCardConfig.Inputs FullyPopulated() => new(
        EndpointUrl: "https://dev.dxsuite.ai",
        CustomerCode: "RAD",
        TenantToken: "dev",
        TimeSourceUrl: "http://dev.dxsuite.ai/api/signage/Time",
        ProxyHostPort: "proxy.customer.local:8080",
        DwsPosition: DwsPositions.OnAlways,
        BootstrapDwsPassword: "s3cret-bootstrap_password",
        HasCustomRootCa: true);

    private static ProvisioningCardConfig.Inputs WiredSiteWithNothingOptional() => new(
        EndpointUrl: "https://dev.dxsuite.ai",
        CustomerCode: "RAD",
        TenantToken: "dev",
        TimeSourceUrl: null,
        ProxyHostPort: null,
        DwsPosition: DwsPositions.OffEntirely,
        BootstrapDwsPassword: "s3cret-bootstrap_password",
        HasCustomRootCa: false);

    private static string Serialize(ProvisioningCardConfig.Inputs inputs, string fingerprint = "abc123")
        => ProvisioningCardConfig.Serialize(ProvisioningCardConfig.Build(inputs), fingerprint);

    [Fact]
    public void EveryKeyTheBootloaderReadsIsPresentAndReadable()
    {
        var raw = Serialize(FullyPopulated());

        foreach (var key in BootloaderKeys)
            Assert.True(raw.Contains($"\"{key}\":"), $"config.json carries no '{key}' key.");

        Assert.Equal("abc123", BootloaderReader.Value(raw, "fingerprint"));
        Assert.Equal("https://dev.dxsuite.ai", BootloaderReader.Value(raw, "endpoint"));
        Assert.Equal("RAD", BootloaderReader.Value(raw, "customer_id"));
        Assert.Equal("proxy.customer.local:8080", BootloaderReader.Value(raw, "proxy"));
        Assert.Equal("http://dev.dxsuite.ai/api/signage/Time", BootloaderReader.Value(raw, "time_server"));
        Assert.Equal("s3cret-bootstrap_password", BootloaderReader.Value(raw, "dws_password"));
    }

    [Fact]
    public void AnUnsetOptionalReadsAsEmptyRatherThanAsTheNextKeysName()
    {
        // The reader takes the next quoted string after the colon, so a null or an unquoted value
        // hands it the following key's name. A player would then call SetProxy("proxy_bypass").
        var raw = Serialize(WiredSiteWithNothingOptional());

        Assert.Equal("", BootloaderReader.Value(raw, "proxy"));
        Assert.Equal("", BootloaderReader.Value(raw, "proxy_bypass"));
        Assert.Equal("", BootloaderReader.Value(raw, "time_server"));
        Assert.Equal("", BootloaderReader.Value(raw, "ca_file"));
        Assert.Equal("", BootloaderReader.Value(raw, "api_key"));

        Assert.DoesNotContain("null", raw);
        Assert.DoesNotContain(": false", raw);
        Assert.DoesNotContain(": true", raw);
    }

    [Theory]
    [InlineData(DwsPositions.OnAlways, "on")]
    [InlineData(DwsPositions.OnAtPark, "on_at_park")]
    [InlineData(DwsPositions.OffEntirely, "off")]
    public void TheDwsPositionReachesTheCardInThePlayersVocabulary(string position, string expected)
    {
        var raw = Serialize(WiredSiteWithNothingOptional() with { DwsPosition = position });

        Assert.Equal(expected, BootloaderReader.Value(raw, "dws"));
    }

    [Fact]
    public void AnUnknownDwsPositionIsRefusedRatherThanWritten()
    {
        Assert.Throws<InvalidOperationException>(
            () => ProvisioningCardConfig.Build(WiredSiteWithNothingOptional() with { DwsPosition = "Sometimes" }));
    }

    [Fact]
    public void TheRecoveryUrlMatchesTheRouteRecoveryControllerServes()
    {
        var raw = Serialize(FullyPopulated());

        // RecoveryController: GET api/Recovery/{tenantToken}. Observed on both bench players.
        Assert.Equal("https://dev.dxsuite.ai/api/Recovery/dev", BootloaderReader.Value(raw, "recovery_url"));
    }

    [Fact]
    public void ATrailingSlashOnTheSiteUrlDoesNotDoubleInTheRecoveryUrl()
    {
        var raw = Serialize(FullyPopulated() with { EndpointUrl = "https://dev.dxsuite.ai/" });

        Assert.Equal("https://dev.dxsuite.ai/api/Recovery/dev", BootloaderReader.Value(raw, "recovery_url"));
    }

    [Fact]
    public void TheCustomRootCaTravelsAsAFileNameAndNeverAsAPem()
    {
        var raw = Serialize(FullyPopulated());

        // A PEM inline would carry newlines, which the reader cannot parse at all.
        Assert.Equal(ProvisioningCardConfig.CustomRootCaFileName, BootloaderReader.Value(raw, "ca_file"));
        Assert.DoesNotContain("BEGIN CERTIFICATE", raw);
    }

    [Fact]
    public void AValueTheReaderCannotParseIsRefusedBeforeItReachesACard()
    {
        var withQuote = FullyPopulated() with { TimeSourceUrl = "http://time.local/\"odd\"" };
        var withNewline = FullyPopulated() with { ProxyHostPort = "proxy.local:8080\nmore" };

        Assert.Throws<InvalidOperationException>(() => Serialize(withQuote));
        Assert.Throws<InvalidOperationException>(() => Serialize(withNewline));
    }

    [Fact]
    public void TheFileIsStillValidJson()
    {
        // Nothing on the player needs this, but an operator or a support engineer opening the card
        // does, and it is free while every value is a plain string.
        using var parsed = JsonDocument.Parse(Serialize(FullyPopulated()));

        Assert.Equal(BootloaderKeys.Length, parsed.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void TheFingerprintCoversTheFirmwarePinAndTheCaContentAsWellAsTheCardKeys()
    {
        var keys = ProvisioningCardConfig.Build(FullyPopulated());
        var baseline = ProvisioningCardConfig.ComputeFingerprint(keys, "xt5", "9.1.151", "PEM-A");

        Assert.Equal(baseline, ProvisioningCardConfig.ComputeFingerprint(keys, "xt5", "9.1.151", "PEM-A"));
        Assert.NotEqual(baseline, ProvisioningCardConfig.ComputeFingerprint(keys, "xt5", "9.1.152", "PEM-A"));
        Assert.NotEqual(baseline, ProvisioningCardConfig.ComputeFingerprint(keys, "hd4", "9.1.151", "PEM-A"));
        Assert.NotEqual(baseline, ProvisioningCardConfig.ComputeFingerprint(keys, "xt5", "9.1.151", "PEM-B"));

        var changedEndpoint = ProvisioningCardConfig.Build(FullyPopulated() with { EndpointUrl = "https://other.dxsuite.ai" });
        Assert.NotEqual(baseline, ProvisioningCardConfig.ComputeFingerprint(changedEndpoint, "xt5", "9.1.151", "PEM-A"));
    }

    /// <summary>
    /// A port of <c>ConfigValue()</c> in autorun.brs: find <c>"key"</c>, take the next quoted string
    /// after the colon, and answer "" for anything it does not understand. Kept deliberately literal,
    /// including the empty-value case, so it stays comparable to the BrightScript line for line.
    /// </summary>
    private static class BootloaderReader
    {
        public static string Value(string raw, string key)
        {
            var at = raw.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (at < 0) return "";

            var colon = raw.IndexOf(':', at);
            if (colon < 0) return "";

            var openQuote = raw.IndexOf('"', colon);
            if (openQuote < 0) return "";

            var closeQuote = raw.IndexOf('"', openQuote + 1);
            if (closeQuote <= openQuote + 1) return "";

            return raw.Substring(openQuote + 1, closeQuote - openQuote - 1);
        }
    }
}
