using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Writes the shown versions of Message Activations (AUD-06): what an Activation puts on screens, recorded at its send
/// and again each time an approved revision changes the Message under it. Rows are only ever added, inside the
/// caller's transaction, so a version exists exactly when the change it records does. <c>FirstServedUtc</c> is left
/// null for the delivery route to stamp (INK-06).
/// </summary>
/// <remarks>
/// <c>tblAsset</c> holds no content hash, so each slot's file is hashed (SHA-256, lowercase hex) when its version is
/// written. A file that cannot be read is recorded with a null hash rather than failing the send or the approval.
/// </remarks>
public sealed class ShownVersions
{
    // Keeps accented letters readable in the stored JSON while still escaping HTML-sensitive characters.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly ITenantMediaPathResolver _mediaPaths;
    private readonly ILogger<ShownVersions> _logger;

    public ShownVersions(ITenantMediaPathResolver mediaPaths, ILogger<ShownVersions> logger)
    {
        _mediaPaths = mediaPaths;
        _logger = logger;
    }

    /// <summary>
    /// Adds the first shown version to a new Activation, saved with it by the caller's next SaveChanges. A canned
    /// Message's texts and assets are read as they are now; a one-off (<paramref name="cannedMessageId"/> null) has
    /// only the texts typed at send.
    /// </summary>
    internal async Task AddFirstAsync(DirectorDbContext db, TblMessageActivation activation, TblMessageType type, int? cannedMessageId,
        IReadOnlyList<(string LanguageCode, string Body)> oneOffTexts, DateTime sentUtc, CancellationToken ct)
    {
        var (texts, assets) = cannedMessageId is { } messageId
            ? await ContentAsync(db, messageId, ct)
            : (oneOffTexts, []);

        activation.Versions.Add(NewVersion(1, ShownVersionReason.FirstSend, null, type, activation.PriorityOverride,
            TextsJson(type, texts), await AssetSlotsJsonAsync(assets, ct), sentUtc));
    }

    /// <summary>
    /// Adds the next shown version, at the Message's RevisionNumber, to every Activation of the Message that is active or
    /// scheduled (<see cref="ActivationState"/>), so one not yet started never publishes the superseded content.
    /// <paramref name="message"/> is the tracked Message holding the approved content, with its Texts and Assets loaded;
    /// the caller's next SaveChanges writes the versions in its transaction.
    /// </summary>
    internal async Task AddRevisionAsync(DirectorDbContext db, TblMessage message, DateTime approvedUtc, CancellationToken ct)
    {
        var live = await db.TblMessageActivations
            .Where(a => a.MessageId == message.MessageId)
            .Where(ActivationState.ActiveOrScheduled(approvedUtc))
            .Select(a => new { a.MessageActivationId, a.PriorityOverride, LastSequence = a.Versions.Max(v => (int?)v.Sequence) ?? 0 })
            .ToListAsync(ct);
        if (live.Count == 0) return;

        var type = await db.TblMessageTypes.AsNoTracking().Include(t => t.Languages)
            .SingleAsync(t => t.MessageTypeId == message.MessageTypeId, ct);
        var assetIds = message.Assets.Select(a => a.AssetId).ToList();
        var files = await db.TblAssets.AsNoTracking().Where(a => assetIds.Contains(a.AssetId)).ToDictionaryAsync(a => a.AssetId, ct);
        var textsJson = TextsJson(type, message.Texts.Select(t => (t.LanguageCode, t.Body)));
        var assetSlotsJson = await AssetSlotsJsonAsync(message.Assets.Select(a => (a.SlotKey, files[a.AssetId])).ToList(), ct);

        foreach (var activation in live)
        {
            var version = NewVersion(activation.LastSequence + 1, ShownVersionReason.Revision, message.RevisionNumber, type,
                activation.PriorityOverride, textsJson, assetSlotsJson, approvedUtc);
            version.MessageActivationId = activation.MessageActivationId;
            db.TblMessageActivationVersions.Add(version);
        }
    }

    // Every version records the type and rank it was shown under, so the Playback Log reads it without the live type.
    private static TblMessageActivationVersion NewVersion(int sequence, ShownVersionReason reason, int? revisionNumber,
        TblMessageType type, int? priorityOverride, string textsJson, string assetSlotsJson, DateTime sentUtc) =>
        new()
        {
            Sequence = sequence,
            Reason = reason,
            RevisionNumber = revisionNumber,
            MessageTypeId = type.MessageTypeId,
            MessageTypeCode = type.Code,
            EffectiveRank = MessageArbitration.EffectiveRank(priorityOverride, type),
            Texts = textsJson,
            AssetSlots = assetSlotsJson,
            SentUtc = sentUtc,
        };

    private static async Task<(IReadOnlyList<(string, string)> Texts, IReadOnlyList<(string SlotKey, TblAsset Asset)> Assets)> ContentAsync(
        DirectorDbContext db, int messageId, CancellationToken ct)
    {
        var texts = await db.TblMessageTexts.AsNoTracking()
            .Where(t => t.MessageId == messageId)
            .Select(t => new { t.LanguageCode, t.Body })
            .ToListAsync(ct);
        var assets = await db.TblMessageAssets.AsNoTracking()
            .Where(a => a.MessageId == messageId)
            .Select(a => new { a.SlotKey, a.Asset })
            .ToListAsync(ct);
        return (texts.Select(t => (t.LanguageCode, t.Body)).ToList(), assets.Select(a => (a.SlotKey, a.Asset)).ToList());
    }

    /// <summary>One text per language code, in the type's language order; a language the type no longer has comes last.</summary>
    private static string TextsJson(TblMessageType type, IEnumerable<(string LanguageCode, string Body)> texts)
    {
        var order = type.Languages.ToDictionary(l => l.LanguageCode, l => l.SortOrder, StringComparer.OrdinalIgnoreCase);
        var ordered = new Dictionary<string, string>();
        foreach (var (code, body) in texts.OrderBy(t => order.GetValueOrDefault(t.LanguageCode, int.MaxValue)).ThenBy(t => t.LanguageCode, StringComparer.Ordinal))
            ordered[code] = body;
        return JsonSerializer.Serialize(ordered, Json);
    }

    private async Task<string> AssetSlotsJsonAsync(IReadOnlyList<(string SlotKey, TblAsset Asset)> assets, CancellationToken ct)
    {
        var slots = new List<object>();
        foreach (var (slotKey, asset) in assets.OrderBy(a => a.SlotKey, StringComparer.OrdinalIgnoreCase))
        {
            slots.Add(new
            {
                slotKey,
                assetId = asset.AssetId,
                fileName = asset.AssetName ?? Path.GetFileName(asset.AssetPath),
                contentHash = await HashAsync(asset, ct),
            });
        }
        return JsonSerializer.Serialize(slots, Json);
    }

    private async Task<string?> HashAsync(TblAsset asset, CancellationToken ct)
    {
        var path = Path.Combine(_mediaPaths.GetRoot(), asset.AssetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        try
        {
            await using var file = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Shown version records asset {AssetId} without a content hash: its file {Path} could not be read",
                asset.AssetId, path);
            return null;
        }
    }
}
