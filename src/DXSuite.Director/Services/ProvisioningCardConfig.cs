using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// The values a provisioning card's <c>config.json</c> carries, and how that file is written.
///
/// The bootloader does not parse JSON: it finds <c>"key"</c> and takes the next quoted string after
/// the colon. So the file must be a flat object of quoted strings under exactly the key names
/// <c>autorun.brs</c> reads, with no nesting, no escapes and no numbers or booleans. A key the
/// bootloader cannot find reads as absent, and a non-string value makes its reader return the next
/// key's name as that key's value. Section 5 of the Engage for BrightSign spec is the contract and
/// <c>config.json</c> at the root of the Engage_BrightSign repository is the template.
/// </summary>
internal static class ProvisioningCardConfig
{
    /// <summary>
    /// Where the customer root CA lands on the card. The bootloader installs a CA by file name, and
    /// the flat format cannot carry a PEM's newlines in any case.
    /// </summary>
    public const string CustomRootCaFileName = "customer-root-ca.pem";

    /// <summary>What an operator's package answers become on the card.</summary>
    public sealed record Inputs(
        string EndpointUrl,
        string CustomerCode,
        string TenantToken,
        string? TimeSourceUrl,
        string? ProxyHostPort,
        string DwsPosition,
        string BootstrapDwsPassword,
        bool HasCustomRootCa);

    /// <summary>
    /// The card's keys, in the order <c>ConfigKeys()</c> in the bootloader lists them so the two can
    /// be read side by side. Every key is always present: the bootloader treats absent and empty
    /// alike, and a full set is what makes a diff against the template meaningful.
    /// </summary>
    public static Dictionary<string, string> Build(Inputs inputs)
        => new()
        {
            ["endpoint"] = Clean(inputs.EndpointUrl, "endpoint"),
            ["customer_id"] = Clean(inputs.CustomerCode, "customer_id"),
            // Enrollment is pairing-only, so a package carries no credential. The bootloader still
            // seeds the key, which keeps a reprovisioned player from inheriting a stale one.
            ["api_key"] = "",
            ["proxy"] = Clean(inputs.ProxyHostPort, "proxy"),
            // No package field feeds this yet; the bootloader reads it only when a proxy is set.
            ["proxy_bypass"] = "",
            ["time_server"] = Clean(inputs.TimeSourceUrl, "time_server"),
            ["dws"] = DwsToken(inputs.DwsPosition),
            ["dws_password"] = Clean(inputs.BootstrapDwsPassword, "dws_password"),
            ["recovery_url"] = RecoveryUrl(inputs.EndpointUrl, inputs.TenantToken),
            ["ca_file"] = inputs.HasCustomRootCa ? CustomRootCaFileName : ""
        };

    /// <summary>
    /// The tenant-scoped recovery URL a provisioned player carries for life, matching
    /// <c>RecoveryController</c>'s route. The bootloader writes it over the per-player takeover URL
    /// on the first healthy boot after enrollment.
    /// </summary>
    public static string RecoveryUrl(string endpointUrl, string tenantToken)
    {
        var endpoint = endpointUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrWhiteSpace(tenantToken))
            return "";

        return Clean($"{endpoint}/api/Recovery/{tenantToken}", "recovery_url");
    }

    /// <summary>
    /// The card's own vocabulary for the DWS position, which is not the database's. The bootloader
    /// compares these three literals directly.
    /// </summary>
    public static string DwsToken(string dwsPosition)
        => dwsPosition switch
        {
            DwsPositions.OnAlways => "on",
            DwsPositions.OnAtPark => "on_at_park",
            DwsPositions.OffEntirely => "off",
            _ => throw new InvalidOperationException($"Unknown DWS position '{dwsPosition}'.")
        };

    /// <summary>
    /// Writes the flat file by hand rather than through a serializer, because the one escape a
    /// serializer would emit is the one thing the bootloader's reader cannot handle. Values are
    /// validated on the way in, so nothing here needs escaping.
    /// </summary>
    public static string Serialize(IReadOnlyDictionary<string, string> cardKeys, string fingerprint)
    {
        var all = new List<KeyValuePair<string, string>>
        {
            new("fingerprint", Clean(fingerprint, "fingerprint"))
        };
        all.AddRange(cardKeys.Select(k => new KeyValuePair<string, string>(k.Key, Clean(k.Value, k.Key))));

        var body = string.Join(",\n", all.Select(k => $"  \"{k.Key}\": \"{k.Value}\""));
        return "{\n" + body + "\n}\n";
    }

    /// <summary>
    /// Covers the applied configuration, never the whole zip, so a new app bundle does not make
    /// every player in the field re-apply. The firmware pin and the CA are hashed although neither
    /// is a card key: the pin because the spec counts it as configuration, and the CA because its
    /// content can change while its file name does not.
    /// </summary>
    public static string ComputeFingerprint(
        IReadOnlyDictionary<string, string> cardKeys,
        string hardwareFamily,
        string? firmwareVersion,
        string? customRootCaPem)
    {
        var canonical = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in cardKeys)
            canonical[pair.Key] = pair.Value;

        canonical["_hardware_family"] = hardwareFamily;
        canonical["_firmware_version"] = firmwareVersion;
        canonical["_custom_root_ca"] = customRootCaPem;

        var json = JsonSerializer.Serialize(canonical);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    /// <summary>
    /// Null becomes empty, which the bootloader reads the same way. Anything a quoted-string reader
    /// would misparse is refused here rather than written to a card, because the failure lands on a
    /// player at a customer site with no way to see what it read.
    /// </summary>
    private static string Clean(string? value, string key)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        foreach (var c in value)
        {
            if (c == '"' || c == '\\' || char.IsControl(c))
                throw new InvalidOperationException(
                    $"The value for '{key}' contains a character the player's config reader cannot parse.");
        }

        return value;
    }
}
