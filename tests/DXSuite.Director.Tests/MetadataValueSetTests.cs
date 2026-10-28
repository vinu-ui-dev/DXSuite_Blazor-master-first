using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Tests;

/// <summary>
/// Every metadata store writes a key as a set of trimmed values, one row each. These pin how an edited
/// set becomes row writes, including the case where a rewrite could collide under the asset store's
/// unique (AssetId, MetadataKey, MetadataValue) index.
/// </summary>
public class MetadataValueSetTests
{
    [Fact]
    public void Values_are_trimmed_blank_ones_dropped_and_case_duplicates_kept_once()
        => Assert.Equal(new[] { "Green", "red" }, MetadataValueSet.Normalize(new[] { " Green ", "", null, "red ", "Red", "green" }));

    [Fact]
    public void Nothing_is_split_on_commas()
        => Assert.Equal(new[] { "Concourse B,Concourse C" }, MetadataValueSet.Normalize(new[] { "Concourse B,Concourse C" }));

    [Fact]
    public void Numeric_looking_values_are_not_normalised()
        => Assert.Equal(new[] { "042", "42" }, MetadataValueSet.Normalize(new[] { "042", "42" }));

    [Fact]
    public void An_unchanged_set_writes_nothing()
        => Assert.True(MetadataValueSet.Reconcile(new[] { "Green", "Red" }, new[] { "Red", "Green" }).IsEmpty);

    [Fact]
    public void A_new_value_is_added_and_an_absent_one_removed_when_no_row_can_be_reused()
    {
        var plan = MetadataValueSet.Reconcile(new[] { "Green", "Red" }, new[] { "Green", "Red", "Blue" });

        Assert.Empty(plan.Updates);
        Assert.Empty(plan.Removes);
        Assert.Equal(new[] { "Blue" }, plan.Adds);
    }

    [Fact]
    public void Clearing_the_set_removes_every_row()
    {
        var plan = MetadataValueSet.Reconcile(new[] { "Green", "Red" }, new[] { " ", "" });

        Assert.Equal(new[] { 0, 1 }, plan.Removes);
        Assert.Empty(plan.Adds);
        Assert.Empty(plan.Updates);
    }

    [Fact]
    public void An_untrimmed_or_differently_cased_row_is_rewritten_in_place()
    {
        var plan = MetadataValueSet.Reconcile(new[] { "Green ", "red" }, new[] { "Green", "Red" });

        Assert.Equal(new[] { (0, "Green"), (1, "Red") }, plan.Updates);
        Assert.Empty(plan.Removes);
        Assert.Empty(plan.Adds);
    }

    [Fact]
    public void A_replaced_value_reuses_the_row_rather_than_deleting_and_inserting()
    {
        var plan = MetadataValueSet.Reconcile(new[] { "Green" }, new[] { "Blue" });

        Assert.Equal(new[] { (0, "Blue") }, plan.Updates);
        Assert.Empty(plan.Removes);
        Assert.Empty(plan.Adds);
    }

    // x is reused for Y only after y has already claimed Y, so no update targets a value another stored
    // row still holds.
    [Fact]
    public void Matching_rows_claim_their_values_before_any_row_is_reused()
    {
        var plan = MetadataValueSet.Reconcile(new[] { "x", "y" }, new[] { "Y" });

        Assert.Equal(new[] { (1, "Y") }, plan.Updates);
        Assert.Equal(new[] { 0 }, plan.Removes);
        Assert.Empty(plan.Adds);
    }

    [Fact]
    public void Duplicate_stored_rows_collapse_to_one()
    {
        var plan = MetadataValueSet.Reconcile(new[] { "Green", "green" }, new[] { "Green" });

        Assert.Empty(plan.Updates);
        Assert.Equal(new[] { 1 }, plan.Removes);
        Assert.Empty(plan.Adds);
    }
}
