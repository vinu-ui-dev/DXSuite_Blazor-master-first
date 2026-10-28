using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// "Uploading as": which identities a user may upload as at a location, whether a pick still holds at
/// write time, the tags the picked identity supplies, and which approval chain it routes to. All pure over
/// rule facts and values, so the picker, the write guard and the chain can never disagree.
/// </summary>
public class UploadIdentityTests
{
    private const int UserId = 14;
    private const int TerminalOps = 3;
    private const int ConcourseOps = 7;

    private static readonly Dictionary<int, string> GroupNames = new()
    {
        [TerminalOps] = "Terminal Ops",
        [ConcourseOps] = "Concourse A Ops"
    };

    private static UploadCandidates Derive(params AccessRuleFact[] rules) =>
        Derive(new HashSet<int> { TerminalOps, ConcourseOps }, rules);

    private static UploadCandidates Derive(HashSet<int> memberOf, params AccessRuleFact[] rules) =>
        UploadIdentityRules.Derive(UserId, isAdmin: false, rules, memberOf, GroupNames);

    private static AccessRuleFact UserRule(int id, string level) => new(id, UserId, null, level);
    private static AccessRuleFact GroupRule(int id, int groupId, string level) => new(id, null, groupId, level);

    // ---------------------------------------------------------------- candidates

    [Fact]
    public void A_location_with_no_rules_is_unrestricted()
        => Assert.Equal(UploadIdentityMode.Unrestricted, Derive().Mode);

    [Fact]
    public void An_administrator_is_never_prompted()
        => Assert.Equal(UploadIdentityMode.Unrestricted,
            UploadIdentityRules.Derive(UserId, isAdmin: true, [UserRule(1, "partial")], new HashSet<int>(), GroupNames).Mode);

    [Fact]
    public void Full_access_through_any_group_is_never_prompted_even_beside_partial_rules()
        => Assert.Equal(UploadIdentityMode.Unrestricted,
            Derive(UserRule(1, "partial"), GroupRule(2, TerminalOps, "full")).Mode);

    [Fact]
    public void Rules_for_other_users_and_groups_do_not_match()
        => Assert.Equal(UploadIdentityMode.Unrestricted,
            Derive(new HashSet<int>(), new AccessRuleFact(1, 99, null, "partial"), GroupRule(2, TerminalOps, "partial")).Mode);

    [Fact]
    public void View_only_access_cannot_upload()
    {
        var candidates = Derive(GroupRule(1, TerminalOps, "view"));

        Assert.Equal(UploadIdentityMode.NotPermitted, candidates.Mode);
        Assert.Empty(candidates.Candidates);
    }

    [Fact]
    public void Each_partial_rule_is_one_candidate_with_myself_first_then_groups_by_name()
    {
        var candidates = Derive(GroupRule(10, TerminalOps, "partial"), UserRule(11, "partial"), GroupRule(12, ConcourseOps, "partial"), GroupRule(13, ConcourseOps, "view"));

        Assert.Equal(UploadIdentityMode.Scoped, candidates.Mode);
        Assert.Equal(
            new[] { ("Myself", (int?)null, 11), ("Concourse A Ops", ConcourseOps, 12), ("Terminal Ops", TerminalOps, 10) },
            candidates.Candidates.Select(c => (c.Label, c.UserGroupId, c.RuleId)));
    }

    [Fact]
    public void Myself_is_offered_only_for_a_direct_user_rule()
        => Assert.DoesNotContain(Derive(GroupRule(10, TerminalOps, "partial")).Candidates, c => c.IsMyself);

    // ---------------------------------------------------------------- write-time guard

    [Fact]
    public void A_picked_group_the_user_still_uploads_as_is_valid()
    {
        var validation = UploadIdentityRules.Validate(Derive(UserRule(11, "partial"), GroupRule(10, TerminalOps, "partial")), TerminalOps);

        Assert.True(validation.IsValid);
        Assert.Equal(10, validation.Identity!.RuleId);
    }

    // The rule was deleted and re-created with a new id: the identity still holds, so the upload does too.
    [Fact]
    public void Validation_matches_the_identity_not_the_rule_row()
        => Assert.True(UploadIdentityRules.Validate(Derive(GroupRule(99, TerminalOps, "partial")), TerminalOps).IsValid);

    [Fact]
    public void A_group_the_user_has_left_fails_rather_than_falling_back_to_no_group()
    {
        var candidates = Derive(new HashSet<int> { ConcourseOps }, UserRule(11, "partial"), GroupRule(10, TerminalOps, "partial"));

        var validation = UploadIdentityRules.Validate(candidates, TerminalOps);

        Assert.False(validation.IsValid);
        Assert.Null(validation.Identity);
        Assert.Contains(candidates.Candidates, c => c.IsMyself);
    }

    [Fact]
    public void Myself_without_a_direct_rule_is_refused()
        => Assert.False(UploadIdentityRules.Validate(Derive(GroupRule(10, TerminalOps, "partial")), pickedGroupId: null).IsValid);

    [Fact]
    public void Myself_with_a_direct_rule_is_valid()
        => Assert.True(UploadIdentityRules.Validate(Derive(UserRule(11, "partial")), pickedGroupId: null).Identity!.IsMyself);

    [Fact]
    public void An_unrestricted_upload_is_valid_only_without_a_group()
    {
        Assert.True(UploadIdentityRules.Validate(UploadCandidates.Unrestricted, null).IsValid);
        Assert.False(UploadIdentityRules.Validate(UploadCandidates.Unrestricted, TerminalOps).IsValid);
    }

    [Fact]
    public void View_only_access_is_refused_whatever_is_picked()
        => Assert.False(UploadIdentityRules.Validate(Derive(GroupRule(1, TerminalOps, "view")), null).IsValid);

    // ---------------------------------------------------------------- values

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(params string[] pairs) =>
        pairs.Select(p => p.Split('=', 2))
            .GroupBy(p => p[0], MetadataEvaluator.KeyComparer)
            .ToDictionary(g => g.Key, g => MetadataEvaluator.NormalizeValues(g.Select(p => p[1])), MetadataEvaluator.KeyComparer);

    private static readonly Dictionary<string, string> FieldTypes = new(MetadataEvaluator.KeyComparer)
    {
        ["Concourse"] = "multi-select",
        ["ServiceCenter"] = "list",
        ["Region"] = "string"
    };

    [Fact]
    public void A_multi_select_key_takes_every_value_the_identity_holds()
        => Assert.Equal(new[] { "Concourse A", "Concourse B" },
            UploadIdentityRules.ValuesFor(["Concourse"], FieldTypes, Values("Concourse=Concourse B", "Concourse=Concourse A"))["Concourse"]);

    [Fact]
    public void Any_other_key_takes_one_value_the_first_in_sort_order()
        => Assert.Equal(new[] { "SLC-A" },
            UploadIdentityRules.ValuesFor(["ServiceCenter"], FieldTypes, Values("ServiceCenter=SLC-B", "ServiceCenter=SLC-A"))["ServiceCenter"]);

    [Fact]
    public void Only_participating_keys_are_tagged()
    {
        var tags = UploadIdentityRules.ValuesFor(["Concourse"], FieldTypes, Values("Concourse=Concourse A", "Region=West"));

        Assert.Equal(new[] { "Concourse" }, tags.Keys);
    }

    [Fact]
    public void A_participating_key_the_identity_holds_no_value_for_is_left_untagged()
        => Assert.Empty(UploadIdentityRules.ValuesFor(["ServiceCenter"], FieldTypes, Values("Concourse=Concourse A")));

    // ---------------------------------------------------------------- explicit tags

    [Fact]
    public void An_explicit_tag_replaces_the_identitys_value_for_that_key_and_adds_others()
    {
        var identity = new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer)
        {
            ["Concourse"] = ["Concourse A", "Concourse B"],
            ["ServiceCenter"] = ["SLC-A"]
        };

        var tags = AssetIngestService.BuildTags(identity, new Dictionary<string, string> { ["concourse"] = " Concourse C ", ["Campaign"] = "Fall", ["Blank"] = " " });

        Assert.Equal(new[] { "Concourse C" }, tags["Concourse"]);
        Assert.Equal(new[] { "SLC-A" }, tags["ServiceCenter"]);
        Assert.Equal(new[] { "Fall" }, tags["Campaign"]);
        Assert.False(tags.ContainsKey("Blank"));
    }

    // ---------------------------------------------------------------- approval chain

    private static readonly AccessRuleFact[] ChainRules =
    [
        UserRule(11, "partial"),
        GroupRule(10, TerminalOps, "partial"),
        GroupRule(12, ConcourseOps, "partial"),
        GroupRule(13, ConcourseOps, "full")
    ];

    [Fact]
    public void A_fresh_pick_routes_to_its_rules_chain_even_when_a_direct_user_rule_has_one()
        => Assert.Equal(10, ApprovalChainRouting.ChooseRule(10, TerminalOps, ChainRules, new HashSet<int> { 10, 11 }));

    [Fact]
    public void Without_a_pick_the_owning_group_is_re_resolved_to_its_current_partial_rule()
        => Assert.Equal(12, ApprovalChainRouting.ChooseRule(null, ConcourseOps, ChainRules, new HashSet<int> { 12, 13 }));

    [Fact]
    public void A_picked_rule_with_no_enabled_chain_falls_through_to_the_owning_group()
        => Assert.Equal(12, ApprovalChainRouting.ChooseRule(99, ConcourseOps, ChainRules, new HashSet<int> { 12 }));

    [Fact]
    public void No_group_and_no_chained_pick_routes_to_the_location_default()
    {
        Assert.Null(ApprovalChainRouting.ChooseRule(null, null, ChainRules, new HashSet<int> { 10, 11, 12 }));
        Assert.Null(ApprovalChainRouting.ChooseRule(11, null, ChainRules, new HashSet<int> { 10 }));
    }

    [Fact]
    public void An_owning_group_with_no_current_rule_routes_to_the_location_default()
        => Assert.Null(ApprovalChainRouting.ChooseRule(null, 42, ChainRules, new HashSet<int> { 10, 11, 12 }));
}
