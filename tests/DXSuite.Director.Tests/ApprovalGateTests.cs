using DXSuite.Director.Services;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Tests;

/// <summary>
/// The manifest approval gate fails open in two deliberate places: a media file with no TblAsset
/// row, and an asset with no TblAssetApproval row, are both treated as legacy content that predates
/// approvals. Both are load-bearing for content uploaded before a location switched approvals on.
/// The cost is that anything which stops a real asset resolving to its row silently publishes it,
/// so the rules that decide "which row is this" are pinned here.
///
/// Approval decides what is published and nothing else. Which player receives a published asset is
/// metadata targeting, asserted by the evaluator's truth table and, per call site, in
/// <see cref="MetadataCallSiteTests"/>.
/// </summary>
public class ApprovalGateTests
{
    [Theory]
    [InlineData("clip.mp4", "/MediaAssets/22/clip.mp4")]
    [InlineData("249cfcc9_test_1080p30_h264.mp4", "/MediaAssets/22/249cfcc9_test_1080p30_h264.mp4")]
    public void An_asset_path_is_built_from_the_file_name_as_stored(string fileName, string expected)
        => Assert.Equal(expected, PlaylistManifestRules.BuildAssetWebPath(22, fileName));

    [Theory]
    [InlineData("450949.White Horse_CC.mp4")]
    [InlineData("PlayerDefault_Landscape 1.png")]
    [InlineData("Ampersand & Co.png")]
    public void A_name_needing_url_encoding_is_still_matched_verbatim(string fileName)
    {
        var path = PlaylistManifestRules.BuildAssetWebPath(22, fileName);

        // Percent-encoding here matched no row, and a file that resolves to no row is published
        // without ever being checked for approval.
        Assert.DoesNotContain("%20", path);
        Assert.DoesNotContain("%26", path);
        Assert.EndsWith(fileName, path);
    }

    [Fact]
    public void A_file_with_spaces_in_its_name_resolves_to_its_row_and_is_gated()
    {
        var records = new Dictionary<string, LocationAssetRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["/MediaAssets/22/450949.White Horse_CC.mp4"] = new(3650, (int)ApprovalStatus.Declined)
        };

        var published = PlaylistManifestRules.SelectPublished(
            [@"D:\Media\MediaAssets\22\450949.White Horse_CC.mp4"], 22, records, enableContentApproval: true);

        Assert.Empty(published);
    }

    [Fact]
    public void Approval_is_not_consulted_where_the_location_has_it_switched_off()
    {
        Assert.True(PlaylistManifestRules.IsPublished(enableContentApproval: false, approvalStatus: (int)ApprovalStatus.Pending));
        Assert.True(PlaylistManifestRules.IsPublished(enableContentApproval: false, approvalStatus: (int)ApprovalStatus.Declined));
    }

    [Theory]
    [InlineData(ApprovalStatus.Pending, false)]
    [InlineData(ApprovalStatus.Declined, false)]
    [InlineData(ApprovalStatus.Withdrawn, false)]
    [InlineData(ApprovalStatus.Approved, true)]
    public void Content_rides_on_its_approval_status(ApprovalStatus status, bool expected)
        => Assert.Equal(expected, PlaylistManifestRules.IsPublished(enableContentApproval: true, approvalStatus: (int)status));

    [Fact]
    public void Content_with_no_recorded_status_predates_approvals_and_stays()
        => Assert.True(PlaylistManifestRules.IsPublished(enableContentApproval: true, approvalStatus: null));

    [Fact]
    public void A_file_with_no_asset_row_predates_approvals_and_stays()
    {
        var published = PlaylistManifestRules.SelectPublished(
            [@"D:\Media\MediaAssets\22\legacy.png"], 22, new Dictionary<string, LocationAssetRecord>(), enableContentApproval: true);

        var (file, record) = Assert.Single(published);
        Assert.EndsWith("legacy.png", file);
        Assert.Null(record);
    }
}
