using DXSuite.Director.Services;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Tests;

/// <summary>
/// One test per call site of the metadata evaluator, proving it ANDs approval where it should and hands
/// the evaluator the right subject. The evaluator's own behaviour is the truth table in
/// <see cref="MetadataEvaluatorTests"/> and is deliberately not re-run here.
///
/// Targeting call sites in this file: the playlist manifest (publication by approval, then delivery by
/// targeting per player) and the in-Director preview (the same two gates, with its pending lens). Scope call
/// sites: every surface that lists a location's assets to a user, through <see cref="ContentScopeContext"/>.
/// </summary>
public class MetadataCallSiteTests
{
    private const int LocationId = MetadataWorldBuilder.LocationId;
    private static readonly DateTime Noon = new(2026, 9, 9, 12, 0, 0);

    private static readonly Dictionary<string, LocationAssetRecord> NoRecords = new(StringComparer.OrdinalIgnoreCase);

    private static string File(string name) => $@"D:\Media\MediaAssets\{LocationId}\{name}";

    private static Dictionary<string, LocationAssetRecord> Records(params (string Name, int AssetId, ApprovalStatus? Status)[] rows) =>
        rows.ToDictionary(
            r => PlaylistManifestRules.BuildAssetWebPath(LocationId, r.Name),
            r => new LocationAssetRecord(r.AssetId, (int?)r.Status),
            StringComparer.OrdinalIgnoreCase);

    // The manifest call site end to end over values: publish by approval, carry the tags, deliver by targeting.
    private static IEnumerable<string?> Manifest(
        MetadataWorldBuilder world, Dictionary<string, LocationAssetRecord> records, bool enableContentApproval,
        Dictionary<string, string> player)
    {
        var candidate = world.BuildPublication();
        var published = PlaylistManifestRules
            .SelectPublished(candidate.Assets.Select(a => File(a.FileName!)), LocationId, records, enableContentApproval)
            .Select(f => Path.GetFileName(f.File))
            .ToHashSet();
        candidate.Assets.RemoveAll(a => !published.Contains(a.FileName!));

        return PlaylistDelivery.Resolve(candidate, world.BuildTargeting(), player, Noon, imageDurationSeconds: null)
            .Assets.Select(a => a.FileName);
    }

    [Fact]
    public void CallSite_Targeting_UnapprovedAssetExcluded()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("approved.png", tags: ["Department=IT"])
            .WithAsset("pending.png", tags: ["Department=IT"]);
        var records = Records(("approved.png", 1, ApprovalStatus.Approved), ("pending.png", 2, ApprovalStatus.Pending));

        var delivered = Manifest(world, records, enableContentApproval: true, MetadataWorldBuilder.Player("Department=IT"));

        Assert.Equal(new[] { "approved.png" }, delivered);
    }

    [Fact]
    public void CallSite_Targeting_ApprovalDisabled_TargetingStillApplies()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("it.png", tags: ["Department=IT"])
            .WithAsset("hr.png", tags: ["Department=HR"]);
        var records = Records(("it.png", 1, ApprovalStatus.Pending), ("hr.png", 2, ApprovalStatus.Pending));

        var delivered = Manifest(world, records, enableContentApproval: false, MetadataWorldBuilder.Player("Department=IT"));

        Assert.Equal(new[] { "it.png" }, delivered);
    }

    [Fact]
    public void CallSite_Targeting_EachPlayerIsItsOwnSubject()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("it.png", tags: ["Department=IT"])
            .WithAsset("hr.png", tags: ["Department=HR"]);

        var itPlayer = Manifest(world, NoRecords, enableContentApproval: false, MetadataWorldBuilder.Player("Department=IT"));
        var hrPlayer = Manifest(world, NoRecords, enableContentApproval: false, MetadataWorldBuilder.Player("Department=HR"));

        Assert.Equal(new[] { "it.png" }, itPlayer);
        Assert.Equal(new[] { "hr.png" }, hrPlayer);
    }

    [Fact]
    public void CallSite_TargetingSwitchOff_TargetingSkipped_ScopeStillApplies()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithTargetingSwitchedOff()
            .WithAsset("it.png", tags: ["Department=IT"])
            .WithAsset("hr.png", tags: ["Department=HR"]);

        var delivered = Manifest(world, NoRecords, enableContentApproval: false, MetadataWorldBuilder.Player("Department=IT"));
        var seen = Scoped(world, "Department=IT");

        Assert.Equal(new[] { "it.png", "hr.png" }, delivered);
        Assert.Equal(new[] { "it.png" }, seen);
    }

    // The scope call sites (Playlist, Asset Library, the schedule asset panel and the Location Content API)
    // all decide through ContentScopeContext, which takes no approval input: Pending content in scope is
    // visible, and ownership rather than approval decides what the viewer may do with it.
    private static IEnumerable<string?> Scoped(MetadataWorldBuilder world, params string[] userTags)
    {
        var scope = new ContentScopeContext(
            new DXSuite.Shared.Authorization.ResolvedAccess { AccessLevel = "partial" },
            world.BuildTargeting().Rules,
            MetadataSubjectResolver.DeviceSubject(MetadataWorldBuilder.Player(userTags)));
        return world.BuildPublication().Assets.Where(a => scope.IsVisible(a.MetadataValues)).Select(a => a.FileName);
    }

    [Fact]
    public void CallSite_Scope_PendingOwnAssetVisible()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("mine-pending.png", tags: ["Department=IT"]);
        var records = Records(("mine-pending.png", 1, ApprovalStatus.Pending));

        var seen = Scoped(world, "Department=IT");
        var published = PlaylistManifestRules.SelectPublished([File("mine-pending.png")], LocationId, records, enableContentApproval: true);

        Assert.Equal(new[] { "mine-pending.png" }, seen);
        Assert.Empty(published);
    }

    [Fact]
    public void CallSite_Scope_PendingAssetOfAnotherUserVisibleWhenInScope()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("theirs-in-scope.png", tags: ["Department=IT"])
            .WithAsset("theirs-out-of-scope.png", tags: ["Department=HR"]);
        var records = Records(("theirs-in-scope.png", 1, ApprovalStatus.Pending), ("theirs-out-of-scope.png", 2, ApprovalStatus.Pending));

        var seen = Scoped(world, "Department=IT");
        var published = PlaylistManifestRules
            .SelectPublished([File("theirs-in-scope.png"), File("theirs-out-of-scope.png")], LocationId, records, enableContentApproval: true);

        Assert.Equal(new[] { "theirs-in-scope.png" }, seen);
        Assert.Empty(published);
    }

    [Fact]
    public void CallSite_Preview_PendingLensAppliesToApprovalOnly()
    {
        var records = Records(("it.png", 1, ApprovalStatus.Pending), ("hr.png", 2, ApprovalStatus.Pending));
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department"))
            .WithAsset("it.png", tags: ["Department=IT"])
            .WithAsset("hr.png", tags: ["Department=HR"]);
        var targeting = world.BuildTargeting();
        var tags = world.BuildPublication().Assets.ToDictionary(a => a.FileName!, a => a.MetadataValues);
        var player = MetadataSubjectResolver.DeviceSubject(MetadataWorldBuilder.Player("Department=IT"));

        var previewed = PlaylistManifestRules
            .SelectPublished([File("it.png"), File("hr.png")], LocationId, records, enableContentApproval: true, treatPendingAsApproved: true)
            .Where(f => PlaylistManifestRules.IsTargetedAt(targeting, tags[Path.GetFileName(f.File)], player))
            .Select(f => Path.GetFileName(f.File));

        Assert.Equal(new[] { "it.png" }, previewed);
    }

    [Fact]
    public void Hole_FileWithNoAssetRow_Included()
    {
        var world = new MetadataWorldBuilder()
            .TargetedBy(new MetadataKeyRule("Department", UntaggedContentPolicy.None))
            .WithAsset("legacy.png", noAssetRow: true);

        var delivered = Manifest(world, NoRecords, enableContentApproval: true, MetadataWorldBuilder.Player("Department=IT"));

        Assert.Equal(new[] { "legacy.png" }, delivered);
    }
}
