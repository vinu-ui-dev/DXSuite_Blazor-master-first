namespace DXSuite.Director.Services;

/// <summary>
/// Which access rule's approval chain an upload goes to. The identity the uploader declared decides, so the
/// chain always agrees with who owns the content.
/// </summary>
public static class ApprovalChainRouting
{
    /// <summary>
    /// The picked rule when it has an enabled chain; otherwise the owning group's current partial rule at the
    /// location when that has one; otherwise null, meaning the location's default chain. Re-resolving the
    /// group rather than storing a rule id keeps a resubmission on the same chain as the first submission
    /// even after an administrator re-creates an equivalent rule.
    /// </summary>
    /// <param name="pickedRuleId">The rule behind a fresh pick, or null when there was none (resubmit, import).</param>
    /// <param name="ownerGroupId">The asset's owning group, or null when it is owned by a user alone.</param>
    /// <param name="rulesWithEnabledChain">Ids of the location's rules that carry an enabled chain.</param>
    public static int? ChooseRule(
        int? pickedRuleId,
        int? ownerGroupId,
        IReadOnlyCollection<AccessRuleFact> locationRules,
        IReadOnlySet<int> rulesWithEnabledChain)
    {
        if (pickedRuleId is int picked && rulesWithEnabledChain.Contains(picked) && locationRules.Any(r => r.RuleId == picked))
            return picked;

        if (ownerGroupId is int group)
        {
            var groupRule = locationRules.FirstOrDefault(r =>
                r.UserGroupId == group && r.AccessLevel == "partial" && rulesWithEnabledChain.Contains(r.RuleId));
            if (groupRule != null)
                return groupRule.RuleId;
        }

        return null;
    }
}
