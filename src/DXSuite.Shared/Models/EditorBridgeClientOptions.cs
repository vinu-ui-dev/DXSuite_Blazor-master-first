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

    /// <summary>
    /// Origins allowed to call /bridge/dxsuite-exchange cross-origin with credentials (Director's
    /// Angular app, e.g. "http://localhost:4200"). Empty by default — the endpoint accepts no
    /// cross-origin callers until a deployment opts a specific origin in.
    /// </summary>
    public string[] AllowedUiOrigins { get; set; } = [];

    /// <summary>
    /// Base URL of Director's Angular app (where /editor-launch lives), e.g. "https://director.example".
    /// Falls back to the first AllowedUiOrigins entry when empty.
    /// </summary>
    public string DirectorUiBaseUrl { get; set; } = "";

    /// <summary>
    /// Optional absolute URL of this host's /bridge/dxsuite-callback. Must equal Clients[ClientId].RedirectUri
    /// on the Director side byte-for-byte. When empty it is built from the incoming request.
    /// </summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>
    /// DXSuite pages that start the Director sign-in automatically for a signed-out user
    /// (the link Director shows opens one of these). Prefix match, case-insensitive.
    /// </summary>
    public string[] EntryPaths { get; set; } = ["/Flight-Data-Editor", "/cupps-agent-screens", "/messages"];
}
