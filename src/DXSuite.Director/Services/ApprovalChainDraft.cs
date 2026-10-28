using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// A chain as the stage editor (<c>ApprovalStageEditor</c>) edits it, for a location's or a Message Type's chain.
/// Settings only one owner has, such as a location's auto-approval of approver uploads, stay with that owner.
/// </summary>
public sealed class ApprovalChainDraft
{
    public bool IsEnabled { get; set; }
    public bool EnableLlmAutoApproval { get; set; }

    /// <summary>0-based order of the last stage LLM auto-approval covers.</summary>
    public int LlmAutoApprovalUpToStageOrder { get; set; }

    public int DeclinedContentRetentionDays { get; set; } = 3;
    public bool EmailApprovers { get; set; }
    public int ReminderIntervalHours { get; set; } = TblApprovalChain.ReminderIntervalHoursDefault;

    /// <summary>Stages in order; a group id of 0 is an unpicked group the editor still shows.</summary>
    public List<StageDefinition> Stages { get; set; } = new();

    /// <summary>The stored chain, or a switched-off one with defaults; either way with at least one stage to edit.</summary>
    public static ApprovalChainDraft From(TblApprovalChain? chain)
    {
        var draft = chain is null
            ? new ApprovalChainDraft()
            : new ApprovalChainDraft
            {
                IsEnabled = chain.IsEnabled,
                EnableLlmAutoApproval = chain.EnableLlmAutoApproval,
                LlmAutoApprovalUpToStageOrder = chain.LlmAutoApprovalUpToStageOrder,
                DeclinedContentRetentionDays = chain.DeclinedContentRetentionDays,
                EmailApprovers = chain.EmailApprovers,
                ReminderIntervalHours = chain.ReminderIntervalHours,
                Stages = chain.Stages
                    .OrderBy(s => s.StageOrder)
                    .Select(s => new StageDefinition
                    {
                        StageOrder = s.StageOrder,
                        StageName = s.StageName,
                        IntraStageOperator = s.IntraStageOperator,
                        GroupIds = s.StageGroups.OrderBy(g => g.SortOrder).Select(g => g.UserGroupId).ToList()
                    })
                    .ToList()
            };
        if (draft.Stages.Count == 0)
            draft.Stages.Add(new StageDefinition { StageOrder = 0, GroupIds = new List<int> { 0 } });
        return draft;
    }

    /// <summary>The stages a save keeps: those with at least one picked group.</summary>
    public int KeptStageCount => Stages.Count(s => s.GroupIds.Any(id => id > 0));

    /// <summary>The stage LLM auto-approval runs up to, kept on a stage that exists.</summary>
    public int ClampedLlmAutoApprovalUpToStageOrder => Math.Clamp(LlmAutoApprovalUpToStageOrder, 0, Math.Max(0, KeptStageCount - 1));

    public int ClampedDeclinedContentRetentionDays => Math.Clamp(DeclinedContentRetentionDays, 1, 90);

    public int ClampedReminderIntervalHours => Math.Max(TblApprovalChain.ReminderIntervalHoursMinimum, ReminderIntervalHours);

    /// <summary>
    /// Copies the draft's settings onto <paramref name="chain"/>, clamped as a chain stores them, and stamps the edit:
    /// the one copy for a location's chain and a Message Type's. The stages are saved separately
    /// (<see cref="ApprovalChainService.ReplaceStagesAsync"/>), as only a change to them should replace them.
    /// </summary>
    public void ApplyTo(TblApprovalChain chain, string editUser, DateTime nowUtc)
    {
        if (chain.ApprovalChainId == 0) chain.CreateDate = nowUtc;
        else chain.LastEditDate = nowUtc;
        chain.LastEditUser = editUser;

        chain.IsEnabled = IsEnabled;
        chain.EnableLlmAutoApproval = EnableLlmAutoApproval;
        chain.LlmAutoApprovalUpToStageOrder = ClampedLlmAutoApprovalUpToStageOrder;
        chain.DeclinedContentRetentionDays = ClampedDeclinedContentRetentionDays;
        chain.EmailApprovers = EmailApprovers;
        chain.ReminderIntervalHours = ClampedReminderIntervalHours;
    }

    /// <summary>Numbers the stages in order and drops unpicked groups, as a save stores them.</summary>
    public void Normalise()
    {
        for (int i = 0; i < Stages.Count; i++)
        {
            Stages[i].StageOrder = i;
            Stages[i].GroupIds.RemoveAll(id => id <= 0);
        }
    }
}
