namespace DXSuite.Shared.Models;

/// <summary>
/// Bound from the "EditorBridgeClient" configuration section. This is DXSuite acting as the
/// Editor-bridge *client*: it registers as one of the "Clients" entries in Director's own
/// EditorBridge config (RadiantCoreWebApi), and uses these settings to redeem the one-time code
/// Director hands the browser, then to keep that bridge session alive.
/// </summary>
public class EditorBridgeClientOptions
{
    public bool Enabled { get; set; }

    /// <summary>Allows http:// (not just https://) for DirectorBaseUrl, loopback only. Dev-only.</summary>
    public bool AllowLoopbackHttp { get; set; }

    /// <summary>Director's base URL, e.g. "https://director.example". No trailing slash.</summary>
    public string DirectorBaseUrl { get; set; } = "";

    /// <summary>Must match this DXSuite instance's key under Director's EditorBridge:Clients.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Must match Clients[ClientId].Secret on the Director side (32+ chars).</summary>
    public string ClientSecret { get; set; } = "";
}
