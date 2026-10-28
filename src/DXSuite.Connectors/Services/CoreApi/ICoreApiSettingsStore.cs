namespace DXSuite.Connectors.Services.CoreApi;

/// <summary>
/// The current tenant's CoreAPI connection, as the store holds it.
/// </summary>
/// <param name="DirectorKey">
/// The CoreAPI key Director holds for the tenant, sent as <c>X-API-KEY</c>. Null when the stored key does not
/// unprotect on this host (it was saved under another Data Protection key ring) and must be entered again.
/// </param>
/// <param name="Servers">CoreAPI server base addresses in the order they are tried.</param>
/// <param name="LastGoodServer">The server that last answered, from any Director instance.</param>
/// <param name="HubUrl">
/// CoreAPI's SignalR hub (C-08). Null when none is configured: the watcher then relies on its poll alone and
/// pages are not updated live.
/// </param>
public sealed record CoreApiSettings(string? DirectorKey, IReadOnlyList<string> Servers, string? LastGoodServer, string? HubUrl = null)
{
    public bool IsKeyReadable => DirectorKey is not null;

    public bool IsConfigured => IsKeyReadable && Servers.Count > 0;

    /// <summary>
    /// Splits a typed list of server addresses (commas, semicolons, spaces or new lines between them) into
    /// base addresses without a trailing slash, keeping their order and dropping repeats.
    /// </summary>
    /// <exception cref="ArgumentException">An entry that is not an absolute http or https address.</exception>
    public static IReadOnlyList<string> ParseServers(string? text) =>
        NormaliseServers((text ?? "").Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    /// <inheritdoc cref="ParseServers"/>
    public static IReadOnlyList<string> NormaliseServers(IEnumerable<string> servers)
    {
        var result = new List<string>();
        foreach (var entry in servers.Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException($"\"{entry}\" is not an http or https address.", nameof(servers));

            var server = entry.TrimEnd('/');
            if (!result.Contains(server, StringComparer.OrdinalIgnoreCase)) result.Add(server);
        }
        return result;
    }

    /// <summary>A typed hub address without a trailing slash, or null when blank.</summary>
    /// <exception cref="ArgumentException">An address that is not an absolute http or https address.</exception>
    public static string? NormaliseHubUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var entry = text.Trim();
        if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"\"{entry}\" is not an http or https address.", nameof(text));
        return entry.TrimEnd('/');
    }

    /// <summary>
    /// The last good server first, so a known-down server does not cost every call its timeout, then the rest
    /// in their configured order.
    /// </summary>
    public IEnumerable<string> ServersInTryOrder()
    {
        var lastGood = Servers.FirstOrDefault(s => string.Equals(s, LastGoodServer, StringComparison.OrdinalIgnoreCase));
        if (lastGood is not null) yield return lastGood;
        foreach (var server in Servers)
        {
            if (!ReferenceEquals(server, lastGood)) yield return server;
        }
    }
}

/// <summary>
/// Per-tenant CoreAPI settings for the current scope, kept in SQL so every Director instance shares them
/// (HOST-04). Implemented by the host that owns the tenant's database.
/// </summary>
public interface ICoreApiSettingsStore
{
    /// <summary>The current tenant's settings, or null when none have been saved.</summary>
    Task<CoreApiSettings?> ReadAsync(CancellationToken ct);

    /// <summary>Replaces the key, the ordered server list and the hub address, protecting the key at rest.</summary>
    /// <exception cref="ArgumentException">
    /// A blank key, no servers, or a server or hub address that is not an http(s) address.
    /// </exception>
    Task SaveAsync(string directorKey, IReadOnlyList<string> servers, string? hubUrl, CancellationToken ct);

    /// <summary>
    /// Sets, changes or (with a blank address) removes the hub address of a saved connection, keeping its key and
    /// servers, so a configured tenant never has to enter its key again for it.
    /// </summary>
    /// <exception cref="ArgumentException">An address that is not an http(s) address.</exception>
    /// <exception cref="InvalidOperationException">No connection has been saved.</exception>
    Task SaveHubUrlAsync(string? hubUrl, CancellationToken ct);

    Task RecordLastGoodServerAsync(string server, CancellationToken ct);
}
