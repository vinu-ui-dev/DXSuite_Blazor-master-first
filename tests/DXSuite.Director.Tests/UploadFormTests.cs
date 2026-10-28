using DXSuite.Director.Services;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Tests;

/// <summary>
/// The upload form's field decisions (cardinality, disable-with-a-reason, the blocked upload), the tags a
/// pick may write, and who may change an asset once it exists. Every creation site renders from these, so
/// the copy is asserted as the operator reads it.
/// </summary>
public class UploadFormTests
{
    private static readonly UploadCandidate Myself = new(11, null, UploadIdentityRules.MyselfLabel);
    private static readonly UploadCandidate ConcourseOps = new(12, 7, "Concourse A Ops");

    private static MetadataDefinitionDto Definition(string key, string label, string type, params string[] options) =>
        new() { MetadataKey = key, DisplayName = label, FieldType = type, Options = options.ToList() };

    private static readonly MetadataDefinitionDto ServiceCenter = Definition("ServiceCenter", "Service Center", "list", "SLC-A", "SLC-B");
    private static readonly MetadataDefinitionDto Concourse = Definition("Concourse", "Concourse", "multi-select", "A", "B", "C");
    private static readonly MetadataDefinitionDto Campaign = Definition("Campaign", "Campaign", "string");

    private static IReadOnlySet<string> Held(params string[] values) => MetadataEvaluator.NormalizeValues(values);

    // ---------------------------------------------------------------- field resolution

    [Fact]
    public void An_unrestricted_upload_offers_the_registry_options_and_starts_empty()
    {
        var field = UploadFormRules.Resolve(new MetadataKeyRule("ServiceCenter"), ServiceCenter, identity: null, held: null, isLibrary: false);

        Assert.Equal(new[] { "SLC-A", "SLC-B" }, field.Options);
        Assert.Empty(field.Selected);
        Assert.False(field.IsDisabled);
        Assert.Null(field.Reason);
    }

    [Fact]
    public void A_multi_select_key_starts_with_every_value_the_identity_holds_and_stays_selectable()
    {
        var field = UploadFormRules.Resolve(new MetadataKeyRule("Concourse"), Concourse, ConcourseOps, Held("B", "A"), isLibrary: false);

        Assert.Equal(new[] { "A", "B" }, field.Options);
        Assert.Equal(new[] { "A", "B" }, field.Selected);
        Assert.False(field.IsDisabled);
        Assert.Equal("Concourse A Ops holds 2 Concourse values. Concourse takes any number, so all of them are used.", field.Reason);
    }

    [Fact]
    public void A_single_valued_key_with_several_values_sets_the_first_as_the_marked_default()
    {
        var field = UploadFormRules.Resolve(new MetadataKeyRule("ServiceCenter"), ServiceCenter, Myself, Held("SLC-B", "SLC-A"), isLibrary: false);

        Assert.Equal(new[] { "SLC-A" }, field.Selected);
        Assert.Equal("SLC-A", field.DefaultValue);
        Assert.False(field.IsDisabled);
        Assert.StartsWith("You hold 2 values for this field and Service Center takes one", field.Reason);
    }

    [Fact]
    public void Exactly_one_value_disables_the_control_with_the_reason()
    {
        var field = UploadFormRules.Resolve(new MetadataKeyRule("ServiceCenter"), ServiceCenter, Myself, Held("SLC-A"), isLibrary: false);

        Assert.True(field.IsDisabled);
        Assert.Null(field.DefaultValue);
        Assert.Equal("You have one Service Center value, so there is nothing to choose.", field.Reason);
    }

    [Fact]
    public void No_value_on_a_key_that_sends_untagged_content_nowhere_blocks_the_upload()
    {
        var rule = new MetadataKeyRule("ServiceCenter", UntaggedContentPolicy.None);

        var field = UploadFormRules.Resolve(rule, ServiceCenter, Myself, Held(), isLibrary: false);

        Assert.True(field.IsBlocking);
        Assert.Equal("Service Center: You have no value for this field, and this location sends content with no value to no players. Ask an administrator to add a Service Center value to your account.", field.Reason);
    }

    [Fact]
    public void The_blocked_copy_names_the_group_with_an_agreeing_verb_and_differs_on_a_library()
    {
        var rule = new MetadataKeyRule("ServiceCenter", UntaggedContentPolicy.None);

        var field = UploadFormRules.Resolve(rule, ServiceCenter, ConcourseOps, Held(), isLibrary: true);

        Assert.Equal("Service Center: Concourse A Ops has no value for this field, and this location hides content with no value from your view. Ask an administrator to add a Service Center value to Concourse A Ops.", field.Reason);
    }

    [Fact]
    public void No_value_on_a_key_that_plays_untagged_content_everywhere_disables_with_the_reassuring_outcome()
    {
        var field = UploadFormRules.Resolve(new MetadataKeyRule("Campaign"), Campaign, Myself, Held(), isLibrary: false);

        Assert.False(field.IsBlocking);
        Assert.True(field.IsDisabled);
        Assert.Equal("Campaign: You have no value for this field. Content with no value plays on all players, so this stays empty.", field.Reason);
    }

    // ---------------------------------------------------------------- picks

    private static readonly Dictionary<string, string> FieldTypes = new(MetadataEvaluator.KeyComparer)
    {
        ["Concourse"] = "multi-select",
        ["ServiceCenter"] = "list"
    };

    private static readonly Dictionary<string, IReadOnlySet<string>> HeldValues = new(MetadataEvaluator.KeyComparer)
    {
        ["Concourse"] = Held("A", "B"),
        ["ServiceCenter"] = Held("SLC-A", "SLC-B")
    };

    [Fact]
    public void Without_picks_every_key_takes_the_identity_default()
    {
        var tags = UploadFormRules.ConstrainPicks(["Concourse", "ServiceCenter"], FieldTypes, HeldValues, picks: null);

        Assert.Equal(new[] { "A", "B" }, tags["Concourse"]);
        Assert.Equal(new[] { "SLC-A" }, tags["ServiceCenter"]);
    }

    [Fact]
    public void A_pick_outside_what_the_identity_holds_is_dropped()
    {
        var picks = new Dictionary<string, List<string>> { ["Concourse"] = ["B", "C"], ["ServiceCenter"] = ["SLC-Z"] };

        var tags = UploadFormRules.ConstrainPicks(["Concourse", "ServiceCenter"], FieldTypes, HeldValues, picks);

        Assert.Equal(new[] { "B" }, tags["Concourse"]);
        Assert.Equal(new[] { "SLC-A" }, tags["ServiceCenter"]);
    }

    [Fact]
    public void A_multi_select_key_may_be_narrowed_to_nothing()
    {
        var picks = new Dictionary<string, List<string>> { ["Concourse"] = [] };

        var tags = UploadFormRules.ConstrainPicks(["Concourse", "ServiceCenter"], FieldTypes, HeldValues, picks);

        Assert.False(tags.ContainsKey("Concourse"));
        Assert.Equal(new[] { "SLC-A" }, tags["ServiceCenter"]);
    }

    [Fact]
    public void A_single_valued_key_keeps_the_value_picked_when_the_identity_holds_it()
        => Assert.Equal(new[] { "SLC-B" },
            UploadFormRules.ConstrainPicks(["ServiceCenter"], FieldTypes, HeldValues,
                new Dictionary<string, List<string>> { ["ServiceCenter"] = ["SLC-B"] })["ServiceCenter"]);

    // ---------------------------------------------------------------- ownership

    private const int Me = 14;
    private static readonly HashSet<int> MyGroups = [7];

    [Theory]
    [InlineData(null)]
    [InlineData("full")]
    public void Full_access_controls_everything_including_unowned_content(string? level)
        => Assert.True(AssetOwnership.CanControl(level, Me, MyGroups, null, null));

    [Fact]
    public void View_access_controls_nothing_and_shows_no_per_asset_reason()
    {
        Assert.False(AssetOwnership.CanControl("view", Me, MyGroups, Me, null));
        Assert.Null(AssetOwnership.LockReason("view", Me, MyGroups, 99, 8, "Terminal Ops"));
    }

    [Fact]
    public void Group_owned_content_is_controllable_by_any_current_member()
    {
        Assert.True(AssetOwnership.CanControl("partial", Me, MyGroups, ownerUserId: 99, ownerGroupId: 7));
        Assert.False(AssetOwnership.CanControl("partial", Me, MyGroups, ownerUserId: Me, ownerGroupId: 8));
    }

    [Fact]
    public void Content_uploaded_as_myself_is_controllable_by_that_user_only()
    {
        Assert.True(AssetOwnership.CanControl("partial", Me, MyGroups, ownerUserId: Me, ownerGroupId: null));
        Assert.False(AssetOwnership.CanControl("partial", Me, MyGroups, ownerUserId: 99, ownerGroupId: null));
    }

    [Fact]
    public void The_reason_names_the_owning_group_never_a_person()
    {
        Assert.Equal("Owned by Terminal Ops.", AssetOwnership.LockReason("partial", Me, MyGroups, 99, 8, "Terminal Ops"));
        Assert.Equal("Owned by another user.", AssetOwnership.LockReason("partial", Me, MyGroups, 99, null, null));
    }

    [Fact]
    public void Unowned_content_states_the_live_rule()
        => Assert.Equal("Only full access users can change this content.", AssetOwnership.LockReason("partial", Me, MyGroups, null, null, null));

    [Fact]
    public void No_reason_is_shown_when_nothing_is_taken_away()
        => Assert.Null(AssetOwnership.LockReason("partial", Me, MyGroups, 99, 7, "Concourse A Ops"));
}
