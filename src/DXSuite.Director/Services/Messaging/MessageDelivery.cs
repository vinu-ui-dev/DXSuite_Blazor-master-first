using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DXSuite.Director.Entities;
using DXSuite.Shared.CustomerDeviations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The delivery document Radiant's Moxie script pulls (INK-03 to INK-08): every Director Display of the tenant, each
/// with the Full screen, Overlay and Ticker entries arbitration gives it now (MSG-10). Liveness is read on every build
/// (MSG-17), so no job has to move Messages on and off screens. The route only serves what this builds, and anything
/// else that needs the document's hash (the doorbell, INK-09) builds it here too, so every reader sees the same bytes.
/// </summary>
/// <remarks>
/// The body holds nothing that changes from one call to the next (INK-04): displays by id, parts in arbitration order,
/// texts in the order their shown version stores them, and times only where an Activation ends. Two instances
/// building it from one database therefore give the same bytes and the same ETag (INK-05, HOST-04).
/// </remarks>
public sealed class MessageDelivery
{
    /// <summary>The body's schema. Fields are only ever added within a schema; removing or changing one raises it.</summary>
    public const int Schema = 1;

    // Accented letters stay readable; HTML-sensitive characters are still escaped. Dictionary keys (language codes)
    // keep their case.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly MessageArbitration _arbitration;
    private readonly DisplayTimeZones _timeZones;
    private readonly bool _gtaa;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessageDelivery> _logger;

    public MessageDelivery(IDbContextFactory<DirectorDbContext> dbFactory, MessageArbitration arbitration, DisplayTimeZones timeZones,
        IOptions<D02GtaaMessagingOptions> d02, TimeProvider clock, ILogger<MessageDelivery> logger)
    {
        _dbFactory = dbFactory;
        _arbitration = arbitration;
        _timeZones = timeZones;
        _gtaa = d02.Value.Enabled;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The document for every display of the tenant as it stands now, serialised and hashed.</summary>
    public Task<DeliveryDocument> BuildAsync(CancellationToken ct) => BuildAsync(null, ct);

    /// <summary>
    /// The document as it stands now, serialised and hashed: for the displays of <paramref name="locationId"/> only when
    /// one is given, else for every display of the tenant. Each scope is its own body with its own ETag.
    /// </summary>
    public async Task<DeliveryDocument> BuildAsync(int? locationId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var displayIds = await db.TblDisplays.AsNoTracking()
            .Where(d => locationId == null || d.LocationId == locationId)
            .OrderBy(d => d.DisplayId).Select(d => d.DisplayId).ToListAsync(ct);
        var candidates = await CandidatesAsync(db, now.UtcDateTime, ct);
        var byDisplay = candidates
            .SelectMany(c => c.DisplayIds.Distinct().Select(displayId => (displayId, c)))
            .ToLookup(p => p.displayId, p => p.c);
        var timeZones = await _timeZones.ForDisplaysAsync(db, displayIds.Where(byDisplay.Contains).ToList(), ct);

        var displays = new List<DeliveryDisplay>(displayIds.Count);
        foreach (var displayId in displayIds)
        {
            if (!byDisplay.Contains(displayId))
            {
                displays.Add(new DeliveryDisplay(displayId, [], [], []));
                continue;
            }

            var timeZone = timeZones[displayId];
            var live = byDisplay[displayId].Where(c => ActivationTiming.IsShowingAt(c.Activation, now, timeZone))
                .ToDictionary(c => c.Activation.MessageActivationId);
            var parts = _arbitration.Arbitrate(live.Values.Select(c => c.Activation));
            displays.Add(new DeliveryDisplay(displayId,
                parts.FullScreen.Select(a => FullScreenEntry(live[a.Activation.MessageActivationId], a.Rank, now, timeZone)).ToList(),
                parts.Overlay.Select(a => Entry(live[a.Activation.MessageActivationId], a.Rank, now, timeZone)).ToList(),
                parts.Ticker.Select(a => Entry(live[a.Activation.MessageActivationId], a.Rank, now, timeZone)).ToList()));
        }

        var body = new DeliveryBody(Schema, displays);
        var json = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        var published = displays.SelectMany(d => d.FullScreen.Concat(d.Overlay).Concat(d.Ticker)).Select(e => e.ShownVersionId).ToHashSet();
        var undelivered = candidates.Where(c => c.Version.FirstServedUtc is null && published.Contains(c.Version.Id))
            .Select(c => c.Version.Id).Order().ToList();
        return new DeliveryDocument(body, json, ConditionalGet.ETagOf(json), undelivered);
    }

    /// <summary>
    /// Stamps Delivered to signage (INK-06) on the document's shown versions that have never been served, once a 200
    /// carrying them has been answered. The update only writes a null, so of two instances serving the same version,
    /// one stamps it and the other changes nothing.
    /// </summary>
    public async Task MarkDeliveredAsync(DeliveryDocument document, CancellationToken ct)
    {
        if (document.UndeliveredVersionIds.Count == 0) return;

        var ids = document.UndeliveredVersionIds;
        var servedUtc = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.TblMessageActivationVersions
            .Where(v => ids.Contains(v.MessageActivationVersionId) && v.FirstServedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FirstServedUtc, servedUtc), ct);
    }

    /// <summary>
    /// Activations that may be live somewhere: not cancelled or over, and for a canned Message still Approved (MSG-17),
    /// each with its newest shown version, which holds the approved content to publish (INK-07). Each screen's clock
    /// then decides whether it shows there now.
    /// </summary>
    private async Task<List<Candidate>> CandidatesAsync(DirectorDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var rows = await db.TblMessageActivations.AsNoTracking()
            .Where(ActivationState.ActiveOrScheduled(nowUtc))
            .Where(a => !a.Message.IsCanned || a.Message.Status == MessageStatus.Approved)
            .Select(a => new
            {
                Activation = a,
                a.Message,
                a.Message.MessageType,
                Version = a.Versions.OrderByDescending(v => v.Sequence)
                    .Select(v => new { v.MessageActivationVersionId, v.Texts, v.FirstServedUtc })
                    .FirstOrDefault(),
                DisplayIds = a.Targets.Where(t => t.DisplayId != null).Select(t => t.DisplayId!.Value).ToList(),
                // The Message's own ASL slot holds its approved video; a pending revision's video is never published.
                AslPath = a.Message.Assets.Where(m => m.SlotKey == MessageTypeConfiguration.AslSlotKey && !m.Asset.IsDeleted)
                    .Select(m => m.Asset.ContentSourceUrl)
                    .FirstOrDefault(),
            })
            .OrderBy(r => r.Activation.MessageActivationId)
            .ToListAsync(ct);

        var candidates = new List<Candidate>(rows.Count);
        foreach (var row in rows)
        {
            // Under D-02 the built-in Counter type rides counter slots outside Display Zone arbitration (MSG-20, INK-12).
            if (MessageTypeConfiguration.IsBuiltInCounter(row.MessageType.Code, _gtaa)) continue;

            // Every send writes version 1 in its transaction, so an Activation without one holds no content Director
            // recorded as approved, and nothing is published for it.
            if (row.Version is null)
            {
                _logger.LogWarning("Activation {ActivationId} has no shown version, so it is not delivered", row.Activation.MessageActivationId);
                continue;
            }

            row.Activation.Message = row.Message;
            row.Message.MessageType = row.MessageType;
            candidates.Add(new Candidate(row.Activation,
                new PublishedVersion(row.Version.MessageActivationVersionId, Texts(row.Version.MessageActivationVersionId, row.Version.Texts),
                    row.Version.FirstServedUtc),
                row.DisplayIds,
                row.AslPath));
        }

        return candidates;
    }

    private static DeliveryEntry Entry(Candidate candidate, int rank, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var activation = candidate.Activation;
        var message = activation.Message;
        var type = message.MessageType;
        return new DeliveryEntry
        {
            Rank = rank,
            TypeCode = type.Code,
            Texts = candidate.Version.Texts,
            TextColour = message.TextColor ?? type.DefaultTextColor,
            BackgroundColour = message.BackgroundColor ?? type.DefaultBackgroundColor,
            // Display and hide seconds are frozen on the Activation at send (MSG-16); the type's are a fallback only.
            ShowSeconds = activation.DisplaySecondsOverride ?? type.DefaultDisplaySeconds,
            HideSeconds = activation.HideSecondsOverride ?? type.DefaultHideSeconds,
            EndUtc = ActivationTiming.CurrentOccurrenceEndUtc(activation, now, timeZone) is { } end ? IsoUtc(end) : null,
            ShownVersionId = candidate.Version.Id,
        };
    }

    // The ASL video is named by its asset's content source path, which the library's content source mode decides (INK-10,
    // INK-16), so the cell points wherever screens read that library from.
    private static DeliveryFullScreenEntry FullScreenEntry(Candidate candidate, int rank, DateTimeOffset now, TimeZoneInfo timeZone) =>
        new(Entry(candidate, rank, now, timeZone)) { AslPath = candidate.AslPath };

    // Whole seconds, so the text is the same however many ticks the stored instant carries.
    private static string IsoUtc(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private IReadOnlyDictionary<string, string> Texts(int versionId, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Shown version {VersionId} holds texts that are not a JSON object of strings; it is delivered with none", versionId);
            return new Dictionary<string, string>();
        }
    }

    private sealed record PublishedVersion(int Id, IReadOnlyDictionary<string, string> Texts, DateTime? FirstServedUtc);

    private sealed record Candidate(TblMessageActivation Activation, PublishedVersion Version, IReadOnlyList<int> DisplayIds, string? AslPath);
}

/// <summary>
/// One build of the delivery document: the body, its serialised bytes exactly as served, their ETag, and the shown
/// versions in it that have never been served.
/// </summary>
public sealed record DeliveryDocument(DeliveryBody Body, byte[] Json, string ETag, IReadOnlyList<int> UndeliveredVersionIds);

/// <summary>The schema 1 body (INK-04): every Director Display of the tenant, ordered by id.</summary>
public sealed record DeliveryBody(int Schema, IReadOnlyList<DeliveryDisplay> Displays);

/// <summary>One display's three Screen Parts; each holds at most <see cref="MessageArbitration.MaxRotating"/> entries, oldest first.</summary>
public sealed record DeliveryDisplay(
    int DisplayId,
    IReadOnlyList<DeliveryFullScreenEntry> FullScreen,
    IReadOnlyList<DeliveryEntry> Overlay,
    IReadOnlyList<DeliveryEntry> Ticker);

/// <summary>What one slot of a part shows (INK-04, section 4.2 "Message slot cells").</summary>
public record DeliveryEntry
{
    /// <summary>The effective rank arbitration used, for information.</summary>
    public required int Rank { get; init; }

    public required string TypeCode { get; init; }

    /// <summary>One text per language code, in the order the shown version stores them.</summary>
    public required IReadOnlyDictionary<string, string> Texts { get; init; }

    /// <summary>Hex, as stored.</summary>
    public required string TextColour { get; init; }

    /// <summary>Hex, as stored.</summary>
    public required string BackgroundColour { get; init; }

    public required int ShowSeconds { get; init; }

    public required int HideSeconds { get; init; }

    /// <summary>ISO 8601 UTC, or null while the Activation runs until cancelled (INK-08).</summary>
    public required string? EndUtc { get; init; }

    /// <summary>The shown version on screen in this slot: the VersionId the Message Playback Log finds.</summary>
    public required int ShownVersionId { get; init; }
}

/// <summary>A Full screen entry, which also names its ASL video (INK-04, INK-10).</summary>
public sealed record DeliveryFullScreenEntry : DeliveryEntry
{
    [SetsRequiredMembers]
    public DeliveryFullScreenEntry(DeliveryEntry entry) : base(entry)
    {
    }

    /// <summary>The ASL video's content source path; null when the Message has none.</summary>
    public string? AslPath { get; init; }
}
