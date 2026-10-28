using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// In-memory heartbeat register for player-consumed Director URLs carrying an
/// `id=` parameter. One monitor id is one logical player URL: the page document
/// and every API poll it makes all beat the same row, so a tagged wall page
/// doesn't splinter into per-endpoint entries. The stored UrlKey is display
/// metadata — page requests win so the row shows the URL the operator actually
/// entered, not an internal poll.
///
/// Every observed request updates memory; the database row is only written when
/// it is at least PersistIntervalSeconds stale, so a 30s poll cadence costs at
/// most one write per minute per id.
/// </summary>
public class UrlHeartbeatService
{
    /// <summary>Sanitization for the operator-assigned monitor id (also applied by the middleware).</summary>
    public static readonly Regex MonitorIdPattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

    private const int PersistIntervalSeconds = 60;

    // Query parameters that vary without changing the logical URL identity.
    private static readonly string[] VolatileParams = ["id", "debug", "refresh", "v"];

    private readonly ConcurrentDictionary<string, HeartbeatEntry> _entries = new();

    private sealed class HeartbeatEntry
    {
        public DateTime LastSeenUtc;
        public DateTime LastPersistedUtc;
        public bool RowKnown;
        public bool PageUrlKnown;
    }

    /// <summary>Latest in-memory observation per monitor id; merged over DB rows by readers.</summary>
    public IReadOnlyDictionary<string, DateTime> Snapshot() =>
        _entries.ToDictionary(kv => kv.Key, kv => kv.Value.LastSeenUtc);

    /// <summary>
    /// Canonical display form of a request URL: path plus its query with volatile
    /// parameters removed and the rest sorted, so parameter order or a debug flag
    /// doesn't change how the same URL reads.
    /// </summary>
    public static string NormalizeUrl(PathString path, IQueryCollection query)
    {
        var parts = query
            .Where(kv => !VolatileParams.Contains(kv.Key.ToLowerInvariant()))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();
        var url = path.Value?.ToLowerInvariant() ?? "";
        if (parts.Count > 0) url += "?" + string.Join("&", parts);
        return url.Length <= 500 ? url : url[..500];
    }

    /// <summary>
    /// Records one observed request for a monitor id. The db context comes from
    /// the request scope so recording is tenant-correct wherever multitenancy
    /// applies. isPageRequest marks page-document URLs (non-/api), which take
    /// over the row's displayed UrlKey.
    /// </summary>
    public async Task RecordAsync(
        IDirectorDBContext db, string monitorId, string urlKey, bool isPageRequest, int? locationId, string? ip)
    {
        var now = DateTime.UtcNow;
        var entry = _entries.GetOrAdd(monitorId, _ => new HeartbeatEntry());

        bool persist;
        lock (entry)
        {
            entry.LastSeenUtc = now;
            // A page request must persist even inside the debounce window when the
            // row hasn't shown a page URL yet, so the display upgrades promptly.
            persist = !entry.RowKnown
                      || (now - entry.LastPersistedUtc).TotalSeconds >= PersistIntervalSeconds
                      || (isPageRequest && !entry.PageUrlKnown);
            if (persist) entry.LastPersistedUtc = now;
        }
        if (!persist) return;

        var row = await db.TblDirectorUrls.FirstOrDefaultAsync(u => u.MonitorId == monitorId);
        if (row == null)
        {
            db.TblDirectorUrls.Add(new TblDirectorUrl
            {
                MonitorId = monitorId,
                UrlKey = urlKey,
                LocationId = locationId,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                LastSeenIp = ip,
                CreateDate = now,
                LastEditDate = now,
            });
        }
        else
        {
            row.LastSeenUtc = now;
            row.LastSeenIp = ip;
            row.LocationId ??= locationId;
            if (isPageRequest) row.UrlKey = urlKey;
            row.LastEditDate = now;
        }

        try
        {
            await db.SaveChangesAsync();
            lock (entry)
            {
                entry.RowKnown = true;
                if (isPageRequest) entry.PageUrlKnown = true;
            }
        }
        catch (DbUpdateException)
        {
            // Concurrent first-sight insert from another request lost the
            // unique-index race; the surviving row picks up the next update.
            lock (entry) entry.RowKnown = true;
        }
    }
}
