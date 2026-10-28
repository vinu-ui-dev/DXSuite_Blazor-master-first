using DXSuite.Director.Entities;
using DXSuite.Director.Services.ContentSource;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The ASL videos Messages bring into the ASL videos Asset Library (INK-10). A video joins the library when its Message
/// saves, ahead of the Message's approval, which also covers the video; so when the Message is rejected or deleted the
/// video leaves the library again, and with it the media share and the library's Ink folder.
/// </summary>
internal static class AslVideos
{
    /// <summary>
    /// Deletes the videos <paramref name="messageId"/> lets go of from their library, inside the caller's transaction: the
    /// asset rows go, and each file is queued for the next sweep of <see cref="PendingFileDeletes"/>. The caller removes
    /// the Message's own slot and revision rows that name them in the same save. A video another Message or its pending
    /// revision still names is kept.
    /// </summary>
    public static Task DeleteAsync(DirectorDbContext db, int messageId, IEnumerable<int> assetIds, DateTime now,
        CancellationToken ct) =>
        DeleteAsync(db, messageId, assetIds, path => PendingFileDeletes.Queue(db, path, now, now), ct);

    /// <summary>
    /// Deletes the videos a newly approved video replaced, as <c>DeleteAsync</c> does, except that each file stays for
    /// the replaced-file grace period: a delivery cell the Moxie script has not yet refreshed may still name it (INK-16).
    /// </summary>
    public static Task DeleteReplacedAsync(DirectorDbContext db, int messageId, IEnumerable<int> assetIds, PendingFileDeletes files,
        CancellationToken ct) =>
        DeleteAsync(db, messageId, assetIds, path => files.QueueReplaced(db, path), ct);

    /// <summary>Whether a Message names the asset as its ASL video.</summary>
    public static Task<bool> IsMessageVideoAsync(DirectorDbContext db, int assetId, CancellationToken ct) =>
        db.TblMessageAssets.AnyAsync(a => a.AssetId == assetId && a.SlotKey == MessageTypeConfiguration.AslSlotKey, ct);

    /// <summary>The asset ids of the ASL video slot rows among <paramref name="rows"/>.</summary>
    public static List<int> IdsIn(IEnumerable<TblMessageAsset> rows) =>
        rows.Where(a => IsAslSlot(a.SlotKey)).Select(a => a.AssetId).ToList();

    /// <inheritdoc cref="IdsIn(IEnumerable{TblMessageAsset})"/>
    public static List<int> IdsIn(IEnumerable<TblMessageRevisionAsset> rows) =>
        rows.Where(a => IsAslSlot(a.SlotKey)).Select(a => a.AssetId).ToList();

    /// <summary>
    /// The videos of <paramref name="before"/> that another video took the place of in <paramref name="after"/>. A slot
    /// that was only emptied replaces nothing: its video stays in the library, as when any slot is cleared.
    /// </summary>
    public static List<int> Replaced(IReadOnlyCollection<int> before, IReadOnlyCollection<int> after) =>
        after.Count == 0 ? [] : before.Except(after).ToList();

    /// <summary>Removes the ASL video slot rows from a Message's assets; returns their asset ids.</summary>
    public static List<int> RemoveFrom(ICollection<TblMessageAsset> rows)
    {
        var videos = rows.Where(a => IsAslSlot(a.SlotKey)).ToList();
        foreach (var row in videos) rows.Remove(row);
        return videos.Select(a => a.AssetId).ToList();
    }

    private static async Task DeleteAsync(DirectorDbContext db, int messageId, IEnumerable<int> assetIds, Action<string> queueFile,
        CancellationToken ct)
    {
        foreach (var assetId in assetIds.Distinct())
        {
            var named = await db.TblMessageAssets.AnyAsync(a => a.AssetId == assetId && a.MessageId != messageId, ct)
                || await db.TblMessageRevisionAssets.AnyAsync(a => a.AssetId == assetId && a.MessageRevision.MessageId != messageId, ct);
            if (named) continue;

            var asset = await db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == assetId, ct);
            if (asset is null) continue;

            await db.RemoveAssetDependentsAsync(asset.AssetId, ct);
            db.TblAssets.Remove(asset);
            queueFile(asset.AssetPath);
        }
    }

    /// <summary>Whether a slot row is the ASL video's (the slot key is case-insensitive, as SQL compares it).</summary>
    private static bool IsAslSlot(string slotKey) =>
        string.Equals(slotKey, MessageTypeConfiguration.AslSlotKey, StringComparison.OrdinalIgnoreCase);
}
