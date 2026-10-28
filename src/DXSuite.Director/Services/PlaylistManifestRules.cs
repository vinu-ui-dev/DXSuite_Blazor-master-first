using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

/// <summary>
/// Whether a playlist file reaches a player, shared by SignageController's manifest path and the
/// in-Director preview so the preview cannot drift from what players play. The answer is two
/// independent gates joined by AND: approval, which is the same for every player and decides the
/// publication, and metadata targeting, which depends on the player and is decided at delivery.
/// Neither gate reads the other, so switching approvals off never switches targeting off.
/// </summary>
public static class PlaylistManifestRules
{
    /// <summary>
    /// The AssetPath recorded for a media file sitting in a location's root media folder.
    /// TblAsset.AssetPath is stored unescaped, so a caller matching a file on disk back to its row
    /// must not percent-encode the name: an encoded name matches nothing for any asset containing a
    /// space, and the manifest gate reads a missing row as ungated legacy content.
    /// </summary>
    public static string BuildAssetWebPath(int locationId, string fileName) =>
        $"/MediaAssets/{locationId}/{fileName}";

    /// <summary>
    /// Whether a file belongs in its location's publication. A file with no asset row, or an asset with
    /// no approval row, is legacy content that predates approvals and stays.
    /// </summary>
    /// <param name="approvalStatus">The asset's approval status, or null when it has no asset or approval row.</param>
    public static bool IsPublished(bool enableContentApproval, int? approvalStatus) =>
        !enableContentApproval
        || approvalStatus is null
        || approvalStatus == (int)ApprovalStatus.Approved;

    /// <summary>
    /// The files at a location that belong in its publication, each paired with its asset row, or null
    /// for a file that has none.
    /// </summary>
    /// <param name="records">The location's asset rows keyed by AssetPath.</param>
    /// <param name="treatPendingAsApproved">The preview's lens. It applies to approval only, never to targeting.</param>
    public static List<(string File, LocationAssetRecord? Record)> SelectPublished(
        IEnumerable<string> files,
        int locationId,
        IReadOnlyDictionary<string, LocationAssetRecord> records,
        bool enableContentApproval,
        bool treatPendingAsApproved = false)
    {
        var published = new List<(string File, LocationAssetRecord? Record)>();
        foreach (var file in files)
        {
            records.TryGetValue(BuildAssetWebPath(locationId, Path.GetFileName(file)), out var record);
            var status = treatPendingAsApproved && record?.ApprovalStatus == (int)ApprovalStatus.Pending
                ? (int)ApprovalStatus.Approved
                : record?.ApprovalStatus;
            if (IsPublished(enableContentApproval, status))
                published.Add((file, record));
        }
        return published;
    }

    /// <summary>
    /// Whether a published asset reaches one player under its location's metadata rules. Always true
    /// while the location's "Target content by metadata" switch is off.
    /// </summary>
    /// <param name="assetValues">The asset's tags, or null for a file with no asset row, which reaches every player.</param>
    public static bool IsTargetedAt(
        LocationTargeting targeting,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? assetValues,
        IReadOnlyDictionary<string, IReadOnlySet<string>> playerValues) =>
        !targeting.Enabled
        || MetadataEvaluator.Evaluate(targeting.Rules, assetValues, playerValues, BlankSubjectPolicy.ApplyKeyRules).Included;
}
