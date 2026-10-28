using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>One off-origin host, and everywhere this tenant will direct a player to it.</summary>
/// <param name="FirstSeen">
/// The earliest creation date among the rows that reference it, which is as close to "when did
/// this host appear" as the authored data can answer. A site diffs it against its last firewall
/// request.
/// </param>
public sealed record OffOriginHost(
    string Host,
    IReadOnlyList<string> Sources,
    IReadOnlyList<int> LocationIds,
    IReadOnlyList<string> DeviceIds,
    DateTime? FirstSeen);

/// <param name="KnownOrigins">What the report measured "off-origin" against, so a reader can tell.</param>
public sealed record OffOriginReport(
    IReadOnlyList<OffOriginHost> Hosts,
    IReadOnlyList<string> KnownOrigins,
    DateTime GeneratedAt);

/// <summary>
/// The off-origin hosts DXSuite will direct a player to. Advisory, never enforcing, and with no
/// player-side consumer: it exists so a site survey can be written before go-live.
///
/// What it does not promise, and the prerequisites page says so: DXSuite can enumerate the hosts it
/// hands the player and can never enumerate what those pages then fetch. A customer dashboard
/// pulling fonts, map tiles and its own API leaves DXSuite seeing one host and the firewall needing
/// four. Chasing the transitive set means a crawler or a proxy log.
/// </summary>
public interface IOffOriginHostReportService
{
    Task<OffOriginReport> DeriveAsync();
}

public sealed class OffOriginHostReportService : IOffOriginHostReportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantMediaPathResolver _mediaPaths;
    private readonly IConfiguration _config;
    private readonly ILogger<OffOriginHostReportService> _logger;

    public OffOriginHostReportService(
        IServiceScopeFactory scopeFactory, ITenantContext tenantContext,
        ITenantMediaPathResolver mediaPaths, IConfiguration config,
        ILogger<OffOriginHostReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _tenantContext = tenantContext;
        _mediaPaths = mediaPaths;
        _config = config;
        _logger = logger;
    }

    public async Task<OffOriginReport> DeriveAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var knownOrigins = BuildKnownOrigins();
        var accumulator = new Dictionary<string, HostAccumulator>(StringComparer.OrdinalIgnoreCase);

        await AddAuthoredAssetsAsync(db, accumulator, knownOrigins);
        await AddZoneLayoutUrlsAsync(db, accumulator, knownOrigins);
        await AddOverrideContentAsync(db, accumulator, knownOrigins);
        await AddFileBasedWebContentAsync(db, accumulator, knownOrigins);

        var hosts = accumulator.Values
            .Select(a => new OffOriginHost(
                a.Host,
                a.Sources.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                a.LocationIds.OrderBy(x => x).ToList(),
                a.DeviceIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                a.FirstSeen))
            .OrderBy(h => h.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OffOriginReport(hosts, knownOrigins, DateTime.UtcNow);
    }

    /// <summary>
    /// What counts as "our own". Three site-URL notions disagree, so the set names which ones it
    /// means: the tenant's configured URL, the global fallback, and anything the tenant declares
    /// as an additional origin of its own.
    ///
    /// An IP-versus-hostname mismatch against the same server counts as off-origin and is reported
    /// rather than normalised away, because on-prem that mismatch is itself something a site survey
    /// should surface.
    /// </summary>
    private List<string> BuildKnownOrigins()
    {
        var origins = new List<string>();

        void Add(string? candidate)
        {
            var host = TryGetHost(candidate);
            if (host != null && !origins.Contains(host, StringComparer.OrdinalIgnoreCase))
                origins.Add(host);
        }

        if (_tenantContext.IsPopulated)
            Add(_tenantContext.SiteUrl);

        Add(_config["FilePaths:SiteUrl"]);

        foreach (var declared in _config.GetSection("DXSuite:OffOrigin:AdditionalOrigins").Get<string[]>() ?? [])
            Add(declared);

        return origins;
    }

    /// <summary>
    /// Web and Stream assets, taken from the authored library with <b>no schedule filter</b>.
    ///
    /// Deriving from a rendered manifest instead is the obvious cheap implementation and it is the
    /// wrong one: the manifest renders what is scheduled now, so a host a scheduler pointed at next
    /// month is invisible today. The survey passes, the firewall request is written, and the site
    /// breaks the day the schedule turns over.
    /// </summary>
    private async Task AddAuthoredAssetsAsync(
        IDirectorDBContext db, Dictionary<string, HostAccumulator> accumulator, List<string> knownOrigins)
    {
        var webAssets = await db.TblAssets
            .Where(a => a.AssetTypeId == (int)Types.AssetType.Web || a.AssetTypeId == (int)Types.AssetType.Stream)
            .Select(a => new { a.AssetPath, a.LocationId, a.CreateDate })
            .ToListAsync();

        foreach (var asset in webAssets)
            Record(accumulator, knownOrigins, asset.AssetPath, "Web or stream asset", asset.LocationId, null, asset.CreateDate);
    }

    /// <summary>Zone layout URLs, held on the location's ExtendedData.</summary>
    private async Task AddZoneLayoutUrlsAsync(
        IDirectorDBContext db, Dictionary<string, HostAccumulator> accumulator, List<string> knownOrigins)
    {
        var locations = await db.TblLocations
            .Where(l => l.ExtendedData != null)
            .Select(l => new { l.LocationId, l.ExtendedData })
            .ToListAsync();

        foreach (var location in locations)
        {
            string? layoutUrl = null;
            try
            {
                var extended = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(location.ExtendedData!);
                if (extended != null && extended.TryGetValue("layoutUrl", out var element)
                    && element.ValueKind == JsonValueKind.String)
                {
                    layoutUrl = element.GetString();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Location {LocationId} has unreadable ExtendedData: {Error}", location.LocationId, ex.Message);
            }

            Record(accumulator, knownOrigins, layoutUrl, "Zone layout URL", location.LocationId, null, null);
        }
    }

    /// <summary>
    /// Override content, which is where a per-device host can hide.
    ///
    /// Derivation cannot start at the location: an override is assigned per display, so one device
    /// can carry a host none of its neighbours do, and a per-location walk that skips overrides is
    /// silently incomplete in exactly the case where one screen was hand-set.
    /// </summary>
    private static async Task AddOverrideContentAsync(
        IDirectorDBContext db, Dictionary<string, HostAccumulator> accumulator, List<string> knownOrigins)
    {
        var overrides = await db.TblOverrideContents
            .Where(o => o.ContentPath.StartsWith("http"))
            .Select(o => new { o.ContentPath, o.DeviceId, o.CreatedDate })
            .ToListAsync();

        foreach (var item in overrides)
            Record(accumulator, knownOrigins, item.ContentPath, "Override content", null, item.DeviceId, item.CreatedDate);
    }

    /// <summary>
    /// The .json web-content definitions on disk, one directory per location.
    ///
    /// Read whole rather than through the scheduling filter, for the same reason as the authored
    /// assets: a definition whose StartDate is next month is a host the firewall still has to allow
    /// before the schedule turns over.
    /// </summary>
    private async Task AddFileBasedWebContentAsync(
        IDirectorDBContext db, Dictionary<string, HostAccumulator> accumulator, List<string> knownOrigins)
    {
        var locationIds = await db.TblLocations.Select(l => l.LocationId).ToListAsync();
        var mediaRoot = _mediaPaths.GetRoot();

        foreach (var locationId in locationIds)
        {
            var folder = Path.Combine(mediaRoot, "MediaAssets", locationId.ToString());
            if (!Directory.Exists(folder))
                continue;

            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                try
                {
                    var definition = JsonSerializer.Deserialize<WebContentDefinition>(
                        await File.ReadAllTextAsync(file));

                    Record(accumulator, knownOrigins, definition?.Url, "File-based web content",
                        locationId, null, File.GetCreationTimeUtc(file));
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _logger.LogWarning("Web content definition {File} could not be read: {Error}", file, ex.Message);
                }
            }
        }
    }

    private static void Record(
        Dictionary<string, HostAccumulator> accumulator, List<string> knownOrigins,
        string? url, string source, int? locationId, string? deviceId, DateTime? createdDate)
    {
        var host = TryGetHost(url);
        if (host == null || knownOrigins.Contains(host, StringComparer.OrdinalIgnoreCase))
            return;

        if (!accumulator.TryGetValue(host, out var entry))
        {
            entry = new HostAccumulator(host);
            accumulator[host] = entry;
        }

        entry.Sources.Add(source);
        if (locationId is { } location) entry.LocationIds.Add(location);
        if (!string.IsNullOrEmpty(deviceId)) entry.DeviceIds.Add(deviceId);

        if (createdDate is { } created && (entry.FirstSeen == null || created < entry.FirstSeen))
            entry.FirstSeen = created;
    }

    /// <summary>
    /// The host of an absolute http(s) URL. A relative path is same-origin by construction and is
    /// not a host at all.
    /// </summary>
    private static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    private sealed class HostAccumulator(string host)
    {
        public string Host { get; } = host;
        public HashSet<string> Sources { get; } = new(StringComparer.Ordinal);
        public HashSet<int> LocationIds { get; } = [];
        public HashSet<string> DeviceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? FirstSeen { get; set; }
    }
}
