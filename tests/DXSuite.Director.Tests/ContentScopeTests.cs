using DXSuite.Director.Services;
using DXSuite.Shared.Authorization;

namespace DXSuite.Director.Tests;

/// <summary>
/// The access-rule half of content scope: which rules scope a user, what a view rule's scope means, and
/// which rules the editor refuses. What scope does to one asset is the evaluator's truth table.
/// </summary>
public class ContentScopeTests
{
    private const string Concourse = "Concourse";
    private const string Airline = "Airline";

    private static LocationMetadataRules Rules(params string[] keys) =>
        new(MetadataCombineMode.All, keys.Select(k => new MetadataKeyRule(k)), keys);

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(params (string Key, string Value)[] pairs) =>
        MetadataSubjectResolver.GroupValues(pairs);

    private static AccessRuleGrant Grant(string level, string? viewScope = null) => new(level, viewScope);

    // ---- Resolve: highest level wins ----

    [Fact]
    public void Resolve_NoMatchingRules_Null()
    {
        Assert.Null(ContentScope.Resolve([]));
    }

    [Fact]
    public void Resolve_FullBeatsEverything()
    {
        var resolved = ContentScope.Resolve([Grant("view", "all"), Grant("full"), Grant("partial")]);

        Assert.Equal(Grant("full"), resolved);
    }

    [Fact]
    public void Resolve_PartialBeatsView_AndKeepsTheViewScope()
    {
        Assert.Equal(Grant("partial", "all"), ContentScope.Resolve([Grant("partial"), Grant("view", "all")]));
        Assert.Equal(Grant("partial", "none"), ContentScope.Resolve([Grant("partial"), Grant("view", "none")]));
        Assert.Equal(Grant("partial"), ContentScope.Resolve([Grant("partial"), Grant("partial")]));
    }

    [Fact]
    public void Resolve_ViewAllTrumpsViewNone()
    {
        Assert.Equal(Grant("view", "all"), ContentScope.Resolve([Grant("view", "none"), Grant("view", "all")]));
        Assert.Equal(Grant("view", "none"), ContentScope.Resolve([Grant("view", "none")]));
    }

    // ---- Which access is evaluated ----

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("full", null, false)]
    [InlineData("partial", null, true)]
    [InlineData("partial", "none", true)]
    [InlineData("partial", "all", false)]
    [InlineData("view", "none", true)]
    [InlineData("view", "all", false)]
    public void Evaluated_OnlyForScopedAndViewNone(string? level, string? viewScope, bool evaluated)
    {
        Assert.Equal(evaluated, ContentScope.IsEvaluated(level, viewScope));
    }

    [Fact]
    public void ViewAll_SeesAssetsOutsideItsValues()
    {
        var context = new ContentScopeContext(
            new ResolvedAccess { AccessLevel = "view", ViewScope = "all" },
            Rules(Concourse),
            Values((Concourse, "Concourse A")));

        Assert.True(context.IsVisible(Values((Concourse, "Concourse B"))));
    }

    [Fact]
    public void ViewNone_ScopedLikePartial()
    {
        var context = new ContentScopeContext(
            new ResolvedAccess { AccessLevel = "view", ViewScope = "none" },
            Rules(Concourse),
            Values((Concourse, "Concourse A")));

        Assert.True(context.IsVisible(Values((Concourse, "Concourse A"))));
        Assert.False(context.IsVisible(Values((Concourse, "Concourse B"))));
    }

    [Fact]
    public void NoRule_SeesEverything()
    {
        var context = ContentScopeContext.Unrestricted(Rules(Concourse));

        Assert.True(context.IsVisible(Values((Concourse, "Concourse B"))));
    }

    // ---- The editor's refusal ----

    [Fact]
    public void Refuse_ScopedRuleWithNoValuesOnAnyParticipatingKey()
    {
        Assert.True(ContentScope.ResolvesToNothing("partial", null, Rules(Concourse, Airline), Values(("Unrelated", "x"))));
        Assert.True(ContentScope.ResolvesToNothing("view", "none", Rules(Concourse, Airline), Values()));
    }

    [Fact]
    public void Refuse_ScopedRuleAtLocationWithNoParticipatingKeys()
    {
        Assert.True(ContentScope.ResolvesToNothing("partial", null, Rules(), Values((Concourse, "Concourse A"))));
    }

    [Fact]
    public void Accept_ScopedRuleWithValuesOnOneKey()
    {
        Assert.False(ContentScope.ResolvesToNothing("partial", null, Rules(Concourse, Airline), Values((Airline, "Delta"))));
    }

    [Fact]
    public void Accept_UnevaluatedRulesWithoutValues()
    {
        Assert.False(ContentScope.ResolvesToNothing("full", null, Rules(Concourse), Values()));
        Assert.False(ContentScope.ResolvesToNothing("view", "all", Rules(Concourse), Values()));
    }
}
