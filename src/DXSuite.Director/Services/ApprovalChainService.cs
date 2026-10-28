using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

public class ApprovalChainService : IApprovalChainService
{
    private readonly IDirectorDBContext _db;
    private readonly IAssetApprovalService _assetApprovalService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ApprovalChainService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublicationInvalidator _publicationInvalidator;

    public ApprovalChainService(
        IDirectorDBContext db,
        IAssetApprovalService assetApprovalService,
        INotificationService notificationService,
        ILogger<ApprovalChainService> logger,
        IServiceScopeFactory scopeFactory,
        IPublicationInvalidator publicationInvalidator)
    {
        _publicationInvalidator = publicationInvalidator;
        _db = db;
        _assetApprovalService = assetApprovalService;
        _notificationService = notificationService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    #region Chain Configuration

    private async Task<TblApprovalChain?> ResolveChainAsync(int assetApprovalId, int locationId, int? pickedRuleId)
    {
        // A resubmission stays on the chain its request already runs on, so reviewers' stages and comments
        // keep their meaning. Routing decides only when there is no such chain, or it has been disabled.
        var existingChainId = await _db.TblApprovalRequests
            .Where(r => r.AssetApprovalId == assetApprovalId)
            .Select(r => (int?)r.ApprovalChainId)
            .FirstOrDefaultAsync();
        if (existingChainId is int chainId)
        {
            var existingChain = await _db.TblApprovalChains
                .Include(c => c.Stages.OrderBy(s => s.StageOrder))
                    .ThenInclude(s => s.StageGroups)
                .FirstOrDefaultAsync(c => c.ApprovalChainId == chainId && c.IsEnabled);
            if (existingChain is { Stages.Count: > 0 })
                return existingChain;
        }

        var ownerGroupId = await _db.TblAssetApprovals
            .Where(a => a.AssetApprovalId == assetApprovalId)
            .Select(a => a.Asset.UploadedByUserGroupId)
            .FirstOrDefaultAsync();

        var rules = await _db.TblLocationAccessRules
            .AsNoTracking()
            .Where(r => r.LocationId == locationId)
            .Select(r => new AccessRuleFact(r.LocationAccessRuleId, r.UserId, r.UserGroupId, r.AccessLevel))
            .ToListAsync();

        var ruleIds = rules.Select(r => r.RuleId).ToList();
        var rulesWithChain = ruleIds.Count == 0
            ? new HashSet<int>()
            : (await _db.TblApprovalChains
                .Where(c => c.LocationAccessRuleId != null && ruleIds.Contains(c.LocationAccessRuleId.Value) && c.IsEnabled)
                .Select(c => c.LocationAccessRuleId!.Value)
                .ToListAsync()).ToHashSet();

        var routedRuleId = ApprovalChainRouting.ChooseRule(pickedRuleId, ownerGroupId, rules, rulesWithChain);

        return routedRuleId is int ruleId
            ? await _db.TblApprovalChains
                .Include(c => c.Stages.OrderBy(s => s.StageOrder))
                    .ThenInclude(s => s.StageGroups)
                .FirstOrDefaultAsync(c => c.LocationAccessRuleId == ruleId && c.IsEnabled)
            : await _db.TblApprovalChains
                .Include(c => c.Stages.OrderBy(s => s.StageOrder))
                    .ThenInclude(s => s.StageGroups)
                .FirstOrDefaultAsync(c => c.LocationId == locationId && c.IsDefault && c.IsEnabled);
    }

    public async Task<TblApprovalChain?> GetChainForLocationAsync(int locationId)
    {
        return await _db.TblApprovalChains
            .Include(c => c.Stages.OrderBy(s => s.StageOrder))
                .ThenInclude(s => s.StageGroups.OrderBy(g => g.SortOrder))
                    .ThenInclude(sg => sg.UserGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.LocationId == locationId && c.IsDefault);
    }

    public async Task<TblApprovalChain?> GetChainForAccessRuleAsync(int locationAccessRuleId)
    {
        return await _db.TblApprovalChains
            .Include(c => c.Stages.OrderBy(s => s.StageOrder))
                .ThenInclude(s => s.StageGroups.OrderBy(g => g.SortOrder))
                    .ThenInclude(sg => sg.UserGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.LocationAccessRuleId == locationAccessRuleId);
    }

    public async Task CreateOrUpdateChainAsync(int locationId, bool isEnabled, bool enableLlmAutoApproval, int llmAutoApprovalUpToStageOrder, bool autoApproveMembers, List<StageDefinition> stages, string editUser, int declinedContentRetentionDays = 3)
    {
        foreach (var s in stages)
            s.GroupIds = s.GroupIds.Where(id => id > 0).ToList();

        if (isEnabled)
        {
            var validation = ValidateChainDefinition(stages);
            if (!validation.IsValid)
                throw new InvalidOperationException(string.Join("; ", validation.Errors));
        }

        var existing = await _db.TblApprovalChains
            .Include(c => c.Stages)
                .ThenInclude(s => s.StageGroups)
            .FirstOrDefaultAsync(c => c.LocationId == locationId && c.IsDefault);

        if (existing != null)
        {
            existing.IsEnabled = isEnabled;
            existing.EnableLlmAutoApproval = enableLlmAutoApproval;
            existing.LlmAutoApprovalUpToStageOrder = llmAutoApprovalUpToStageOrder;
            existing.AutoApproveMembers = autoApproveMembers;
            existing.DeclinedContentRetentionDays = Math.Clamp(declinedContentRetentionDays, 1, 90);
            existing.LastEditDate = DateTime.UtcNow;
            existing.LastEditUser = editUser;

            var stageIds = existing.Stages.Select(s => s.ApprovalStageId).ToList();

            var referencingRequests = await _db.TblApprovalRequests
                .Where(r => r.CurrentStageId != null && stageIds.Contains(r.CurrentStageId.Value))
                .ToListAsync();
            foreach (var req in referencingRequests)
                req.CurrentStageId = null;

            var referencingActions = await _db.TblApprovalActions
                .Where(a => stageIds.Contains(a.ApprovalStageId))
                .ToListAsync();
            _db.TblApprovalActions.RemoveRange(referencingActions);

            var referencingQueueEntries = await _db.TblApprovalQueueEntries
                .Where(q => stageIds.Contains(q.ApprovalStageId))
                .ToListAsync();
            _db.TblApprovalQueueEntries.RemoveRange(referencingQueueEntries);

            _db.TblApprovalStages.RemoveRange(existing.Stages);
            await _db.SaveChangesAsync();

            foreach (var stageDef in stages.Where(s => s.GroupIds.Count > 0))
            {
                var stage = new TblApprovalStage
                {
                    ApprovalChainId = existing.ApprovalChainId,
                    StageOrder = stageDef.StageOrder,
                    StageName = stageDef.StageName,
                    IntraStageOperator = stageDef.IntraStageOperator
                };
                _db.TblApprovalStages.Add(stage);
                await _db.SaveChangesAsync();

                int sortOrder = 0;
                foreach (var groupId in stageDef.GroupIds)
                {
                    _db.TblApprovalStageGroups.Add(new TblApprovalStageGroup
                    {
                        ApprovalStageId = stage.ApprovalStageId,
                        UserGroupId = groupId,
                        SortOrder = sortOrder++
                    });
                }
            }
        }
        else
        {
            var chain = new TblApprovalChain
            {
                LocationId = locationId,
                IsEnabled = isEnabled,
                EnableLlmAutoApproval = enableLlmAutoApproval,
                LlmAutoApprovalUpToStageOrder = llmAutoApprovalUpToStageOrder,
                AutoApproveMembers = autoApproveMembers,
                DeclinedContentRetentionDays = Math.Clamp(declinedContentRetentionDays, 1, 90),
                CreateDate = DateTime.UtcNow,
                LastEditUser = editUser
            };
            _db.TblApprovalChains.Add(chain);
            await _db.SaveChangesAsync();

            foreach (var stageDef in stages.Where(s => s.GroupIds.Count > 0))
            {
                var stage = new TblApprovalStage
                {
                    ApprovalChainId = chain.ApprovalChainId,
                    StageOrder = stageDef.StageOrder,
                    StageName = stageDef.StageName,
                    IntraStageOperator = stageDef.IntraStageOperator
                };
                _db.TblApprovalStages.Add(stage);
                await _db.SaveChangesAsync();

                int sortOrder = 0;
                foreach (var groupId in stageDef.GroupIds)
                {
                    _db.TblApprovalStageGroups.Add(new TblApprovalStageGroup
                    {
                        ApprovalStageId = stage.ApprovalStageId,
                        UserGroupId = groupId,
                        SortOrder = sortOrder++
                    });
                }
            }
        }

        await _db.SaveChangesAsync();

        // Sync TblLocation.EnableContentApproval so downstream components pick up the setting
        var location = await _db.TblLocations.FirstOrDefaultAsync(l => l.LocationId == locationId);
        if (location != null)
        {
            location.EnableContentApproval = isEnabled;
            await _db.SaveChangesAsync();
            _publicationInvalidator.LocationChanged(locationId);
        }
    }

    public async Task DeleteChainAsync(int locationId)
    {
        var chain = await _db.TblApprovalChains.FirstOrDefaultAsync(c => c.LocationId == locationId && c.IsDefault);
        if (chain != null)
        {
            _db.TblApprovalChains.Remove(chain);
            await _db.SaveChangesAsync();
        }
    }

    public ChainValidationResult ValidateChainDefinition(List<StageDefinition> stages)
    {
        var result = new ChainValidationResult { IsValid = true };

        if (stages.Count == 0)
        {
            result.IsValid = false;
            result.Errors.Add("At least one stage is required.");
            return result;
        }

        var allGroupIds = new HashSet<int>();
        for (int i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            if (stage.GroupIds.Count == 0)
            {
                result.IsValid = false;
                result.Errors.Add($"Stage {i + 1} has no groups assigned.");
            }

            if (stage.IntraStageOperator != "AND" && stage.IntraStageOperator != "OR")
            {
                result.IsValid = false;
                result.Errors.Add($"Stage {i + 1} has invalid operator '{stage.IntraStageOperator}'.");
            }

            foreach (var gid in stage.GroupIds)
            {
                if (!allGroupIds.Add(gid))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Group ID {gid} appears in multiple stages.");
                }
            }
        }

        return result;
    }

    #endregion

    #region Chain Execution

    public async Task<TblApprovalRequest?> SubmitToChainAsync(int assetApprovalId, int locationId, bool passedLlmModeration = false, int? uploadingUserId = null, int? pickedRuleId = null)
    {
        var chain = await ResolveChainAsync(assetApprovalId, locationId, pickedRuleId);

        if (chain == null || chain.Stages.Count == 0)
            return null;

        var orderedStages = chain.Stages.OrderBy(s => s.StageOrder).ToList();

        if (chain.AutoApproveMembers && uploadingUserId.HasValue && uploadingUserId.Value > 0)
        {
            var approverGroupIds = orderedStages
                .SelectMany(s => s.StageGroups.Select(sg => sg.UserGroupId))
                .Distinct()
                .ToList();

            var isApproverMember = await _db.TblUserGroupMemberships
                .AnyAsync(m => m.UserId == uploadingUserId.Value && approverGroupIds.Contains(m.UserGroupId));

            if (isApproverMember)
            {
                await _assetApprovalService.ApproveAssetAsync(assetApprovalId, 0, "Auto-approved (uploader is an approver group member)");
                _logger.LogInformation("Asset approval {Id} auto-approved — uploader {UserId} is in approver group for location {LocationId}",
                    assetApprovalId, uploadingUserId.Value, locationId);
                return null;
            }
        }

        var existingRequest = await _db.TblApprovalRequests
            .FirstOrDefaultAsync(r => r.AssetApprovalId == assetApprovalId);

        if (existingRequest != null && existingRequest.Status == "pending")
            return existingRequest;

        if (chain.EnableLlmAutoApproval && passedLlmModeration)
        {
            var autoApprovedStages = orderedStages
                .Where(s => s.StageOrder <= chain.LlmAutoApprovalUpToStageOrder)
                .ToList();

            if (autoApprovedStages.Count >= orderedStages.Count)
            {
                await _assetApprovalService.ApproveAssetAsync(assetApprovalId, 0, "Auto-approved by LLM content moderation");
                _logger.LogInformation("Asset approval {Id} fully auto-approved via LLM moderation for location {LocationId}",
                    assetApprovalId, locationId);

                var completedRequest = await GetOrResetRequest(existingRequest, assetApprovalId, chain.ApprovalChainId);
                completedRequest.Status = "approved";
                completedRequest.SubmittedDate = DateTime.UtcNow;
                completedRequest.CompletedDate = DateTime.UtcNow;
                completedRequest.CurrentStageId = null;
                await _db.SaveChangesAsync();
                return completedRequest;
            }

            var firstManualStage = orderedStages.First(s => s.StageOrder > chain.LlmAutoApprovalUpToStageOrder);

            var request = await GetOrResetRequest(existingRequest, assetApprovalId, chain.ApprovalChainId);
            request.CurrentStageId = firstManualStage.ApprovalStageId;
            request.Status = "pending";
            request.SubmittedDate = DateTime.UtcNow;
            request.CompletedDate = null;
            await _db.SaveChangesAsync();

            foreach (var stageGroup in firstManualStage.StageGroups)
            {
                _db.TblApprovalQueueEntries.Add(new TblApprovalQueueEntry
                {
                    ApprovalRequestId = request.ApprovalRequestId,
                    ApprovalStageId = firstManualStage.ApprovalStageId,
                    UserGroupId = stageGroup.UserGroupId,
                    Status = "pending",
                    CreatedDate = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Asset approval {Id} auto-approved stages 0-{AutoStage} via LLM moderation, manual review starting at stage {ManualStage} for location {LocationId}",
                assetApprovalId, chain.LlmAutoApprovalUpToStageOrder, firstManualStage.StageOrder, locationId);

            var manualGroupIds = firstManualStage.StageGroups.Select(sg => sg.UserGroupId);
            await NotifyGroupMembersAsync(manualGroupIds, "New content pending approval",
                "New content has been submitted for your review (auto-approved through earlier stages).", assetApprovalId, "/Approvals");

            return request;
        }

        var firstStage = orderedStages.First();

        var normalRequest = await GetOrResetRequest(existingRequest, assetApprovalId, chain.ApprovalChainId);
        normalRequest.CurrentStageId = firstStage.ApprovalStageId;
        normalRequest.Status = "pending";
        normalRequest.SubmittedDate = DateTime.UtcNow;
        normalRequest.CompletedDate = null;
        await _db.SaveChangesAsync();

        foreach (var stageGroup in firstStage.StageGroups)
        {
            _db.TblApprovalQueueEntries.Add(new TblApprovalQueueEntry
            {
                ApprovalRequestId = normalRequest.ApprovalRequestId,
                ApprovalStageId = firstStage.ApprovalStageId,
                UserGroupId = stageGroup.UserGroupId,
                Status = "pending",
                CreatedDate = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        _logger.LogInformation("Submitted approval request {RequestId} for asset approval {ApprovalId}, chain {ChainId}, stage 1",
            normalRequest.ApprovalRequestId, assetApprovalId, chain.ApprovalChainId);

        var groupIds = firstStage.StageGroups.Select(sg => sg.UserGroupId);
        await NotifyGroupMembersAsync(groupIds, "New content pending approval",
            "New content has been submitted for your review.", assetApprovalId, "/Approvals");

        return normalRequest;
    }

    private async Task<TblApprovalRequest> GetOrResetRequest(TblApprovalRequest? existing, int assetApprovalId, int chainId)
    {
        if (existing != null)
        {
            existing.ApprovalChainId = chainId;
            return existing;
        }

        var request = new TblApprovalRequest
        {
            AssetApprovalId = assetApprovalId,
            ApprovalChainId = chainId
        };
        _db.TblApprovalRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    public async Task<ApprovalActionResult> ApproveAsync(int queueEntryId, int actingUserId, int actingGroupId, string? notes = null)
    {
        var entry = await _db.TblApprovalQueueEntries
            .Include(e => e.ApprovalRequest)
                .ThenInclude(r => r.ApprovalChain)
                    .ThenInclude(c => c.Stages.OrderBy(s => s.StageOrder))
                        .ThenInclude(s => s.StageGroups)
            .Include(e => e.ApprovalStage)
            .FirstOrDefaultAsync(e => e.QueueEntryId == queueEntryId);

        if (entry == null)
            return new ApprovalActionResult { Success = false, Outcome = "error", Message = "Queue entry not found." };

        if (entry.Status != "pending")
            return new ApprovalActionResult { Success = true, Outcome = "already-resolved", Message = "This item has already been resolved." };

        if (entry.UserGroupId != actingGroupId)
            return new ApprovalActionResult { Success = false, Outcome = "error", Message = "Group mismatch." };

        var isMember = await _db.TblUserGroupMemberships
            .AnyAsync(m => m.UserGroupId == actingGroupId && m.UserId == actingUserId);
        if (!isMember)
            return new ApprovalActionResult { Success = false, Outcome = "error", Message = "User is not a member of this group." };

        _db.TblApprovalActions.Add(new TblApprovalAction
        {
            ApprovalRequestId = entry.ApprovalRequestId,
            ApprovalStageId = entry.ApprovalStageId,
            UserGroupId = actingGroupId,
            ActingUserId = actingUserId,
            ActionType = "approved",
            Notes = notes,
            ActionDate = DateTime.UtcNow
        });

        entry.Status = "approved";
        entry.ResolvedDate = DateTime.UtcNow;
        entry.ResolvedByUserId = actingUserId;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ApprovalActionResult { Success = true, Outcome = "already-resolved", Message = "Another user acted on this item first." };
        }

        var request = entry.ApprovalRequest;
        var stage = entry.ApprovalStage;

        if (stage.IntraStageOperator == "OR")
        {
            var remainingPending = await _db.TblApprovalQueueEntries
                .Where(e => e.ApprovalRequestId == request.ApprovalRequestId
                    && e.ApprovalStageId == stage.ApprovalStageId
                    && e.Status == "pending")
                .ToListAsync();

            foreach (var remaining in remainingPending)
            {
                remaining.Status = "superseded";
                remaining.ResolvedDate = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            return await AdvanceOrCompleteChainAsync(request);
        }
        else
        {
            var pendingCount = await _db.TblApprovalQueueEntries
                .CountAsync(e => e.ApprovalRequestId == request.ApprovalRequestId
                    && e.ApprovalStageId == stage.ApprovalStageId
                    && e.Status == "pending");

            if (pendingCount == 0)
            {
                return await AdvanceOrCompleteChainAsync(request);
            }

            return new ApprovalActionResult { Success = true, Outcome = "group-approved", Message = "Your group's approval recorded. Waiting for other groups." };
        }
    }

    public async Task<ApprovalActionResult> DeclineAsync(int queueEntryId, int actingUserId, int actingGroupId, string? notes = null)
    {
        var entry = await _db.TblApprovalQueueEntries
            .Include(e => e.ApprovalRequest)
            .FirstOrDefaultAsync(e => e.QueueEntryId == queueEntryId);

        if (entry == null)
            return new ApprovalActionResult { Success = false, Outcome = "error", Message = "Queue entry not found." };

        if (entry.Status != "pending")
            return new ApprovalActionResult { Success = true, Outcome = "already-resolved", Message = "This item has already been resolved." };

        if (entry.UserGroupId != actingGroupId)
            return new ApprovalActionResult { Success = false, Outcome = "error", Message = "Group mismatch." };

        var isMember = await _db.TblUserGroupMemberships
            .AnyAsync(m => m.UserGroupId == actingGroupId && m.UserId == actingUserId);
        if (!isMember)
            return new ApprovalActionResult { Success = false, Outcome = "error", Message = "User is not a member of this group." };

        _db.TblApprovalActions.Add(new TblApprovalAction
        {
            ApprovalRequestId = entry.ApprovalRequestId,
            ApprovalStageId = entry.ApprovalStageId,
            UserGroupId = actingGroupId,
            ActingUserId = actingUserId,
            ActionType = "declined",
            Notes = notes,
            ActionDate = DateTime.UtcNow
        });

        entry.Status = "declined";
        entry.ResolvedDate = DateTime.UtcNow;
        entry.ResolvedByUserId = actingUserId;

        var remainingPending = await _db.TblApprovalQueueEntries
            .Where(e => e.ApprovalRequestId == entry.ApprovalRequestId
                && e.Status == "pending"
                && e.QueueEntryId != queueEntryId)
            .ToListAsync();

        foreach (var remaining in remainingPending)
        {
            remaining.Status = "superseded";
            remaining.ResolvedDate = DateTime.UtcNow;
        }

        var request = entry.ApprovalRequest;
        request.Status = "declined";
        request.CurrentStageId = null;
        request.CompletedDate = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ApprovalActionResult { Success = true, Outcome = "already-resolved", Message = "Another user acted on this item first." };
        }

        await _assetApprovalService.DeclineAssetAsync(request.AssetApprovalId, actingUserId, notes);

        _logger.LogInformation("Approval request {RequestId} declined by user {UserId} (group {GroupId})",
            request.ApprovalRequestId, actingUserId, actingGroupId);

        var declinedUploader = await _db.TblAssetApprovals
            .AsNoTracking()
            .Where(a => a.AssetApprovalId == request.AssetApprovalId)
            .Select(a => a.UploadedByUserId)
            .FirstOrDefaultAsync();
        if (declinedUploader > 0)
        {
            await _notificationService.CreateNotificationAsync(
                declinedUploader, "Content declined",
                notes ?? "Your content has been declined. You can re-submit after making changes.",
                "approval-declined", request.AssetApprovalId, "/Approvals");
        }

        return new ApprovalActionResult { Success = true, Outcome = "chain-declined", Message = "Content has been declined." };
    }

    private async Task<ApprovalActionResult> AdvanceOrCompleteChainAsync(TblApprovalRequest request)
    {
        var allStages = request.ApprovalChain.Stages.OrderBy(s => s.StageOrder).ToList();
        var currentStageIndex = allStages.FindIndex(s => s.ApprovalStageId == request.CurrentStageId);

        if (currentStageIndex < allStages.Count - 1)
        {
            var nextStage = allStages[currentStageIndex + 1];
            request.CurrentStageId = nextStage.ApprovalStageId;

            foreach (var stageGroup in nextStage.StageGroups)
            {
                _db.TblApprovalQueueEntries.Add(new TblApprovalQueueEntry
                {
                    ApprovalRequestId = request.ApprovalRequestId,
                    ApprovalStageId = nextStage.ApprovalStageId,
                    UserGroupId = stageGroup.UserGroupId,
                    Status = "pending",
                    CreatedDate = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();

            _logger.LogInformation("Approval request {RequestId} advanced to stage {StageOrder}",
                request.ApprovalRequestId, nextStage.StageOrder);

            var nextGroupIds = nextStage.StageGroups.Select(sg => sg.UserGroupId);
            await NotifyGroupMembersAsync(nextGroupIds, "New content pending approval",
                "Content has advanced to your review stage.", request.AssetApprovalId, "/Approvals");

            return new ApprovalActionResult { Success = true, Outcome = "stage-complete", Message = $"Stage complete. Advanced to stage {nextStage.StageOrder + 1}." };
        }
        else
        {
            request.Status = "approved";
            request.CurrentStageId = null;
            request.CompletedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _assetApprovalService.ApproveAssetAsync(request.AssetApprovalId, 0, "Approved via approval chain");

            _logger.LogInformation("Approval request {RequestId} completed — asset approved", request.ApprovalRequestId);

            var approvedAsset = await _db.TblAssetApprovals
                .AsNoTracking()
                .Where(a => a.AssetApprovalId == request.AssetApprovalId)
                .Select(a => a.UploadedByUserId)
                .FirstOrDefaultAsync();
            if (approvedAsset > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    approvedAsset, "Content approved",
                    "Your content has been approved and is now live.",
                    "approval-approved", request.AssetApprovalId, "/Approvals");
            }

            return new ApprovalActionResult { Success = true, Outcome = "chain-approved", Message = "All stages approved. Content is now live." };
        }
    }

    #endregion

    #region Query

    public async Task<List<ApprovalQueueItem>> GetPendingItemsForUserAsync(int userId)
    {
        var userGroupIds = await _db.TblUserGroupMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.UserGroupId)
            .ToListAsync();

        if (userGroupIds.Count == 0)
            return new List<ApprovalQueueItem>();

        return await BuildQueueItemsQuery(
            filter: e => userGroupIds.Contains(e.UserGroupId) && e.Status == "pending")
            .ToListAsync();
    }

    public async Task<List<ApprovalQueueItem>> GetAllPendingItemsAsync()
    {
        return await BuildQueueItemsQuery(
            filter: e => e.Status == "pending")
            .ToListAsync();
    }

    private IQueryable<ApprovalQueueItem> BuildQueueItemsQuery(
        System.Linq.Expressions.Expression<Func<TblApprovalQueueEntry, bool>>? filter = null)
    {
        IQueryable<TblApprovalQueueEntry> query = _db.TblApprovalQueueEntries
            .AsNoTracking();

        if (filter != null)
            query = query.Where(filter);

        return query
            .Include(e => e.ApprovalRequest)
                .ThenInclude(r => r.AssetApproval)
                    .ThenInclude(a => a.Asset)
            .Include(e => e.ApprovalRequest)
                .ThenInclude(r => r.AssetApproval)
                    .ThenInclude(a => a.UploadedByUser)
            .Include(e => e.ApprovalRequest)
                .ThenInclude(r => r.ApprovalChain)
                    .ThenInclude(c => c.Location)
            .Include(e => e.ApprovalRequest)
                .ThenInclude(r => r.ApprovalChain)
                    .ThenInclude(c => c.Stages)
            .Include(e => e.UserGroup)
            .OrderByDescending(e => e.CreatedDate)
            .Select(e => new ApprovalQueueItem
            {
                QueueEntryId = e.QueueEntryId,
                ApprovalRequestId = e.ApprovalRequestId,
                AssetApprovalId = e.ApprovalRequest.AssetApprovalId,
                AssetId = e.ApprovalRequest.AssetApproval.AssetId,
                AssetName = e.ApprovalRequest.AssetApproval.Asset.AssetName,
                AssetPath = e.ApprovalRequest.AssetApproval.Asset.AssetPath,
                LocationName = e.ApprovalRequest.ApprovalChain.Location.LocationName,
                UploadedByName = e.ApprovalRequest.AssetApproval.UploadedByUser.FriendlyName,
                SubmittedDate = e.ApprovalRequest.SubmittedDate,
                GroupId = e.UserGroupId,
                GroupName = e.UserGroup.GroupName,
                CurrentStageOrder = e.ApprovalRequest.ApprovalChain.Stages
                    .Where(s => s.ApprovalStageId == e.ApprovalRequest.CurrentStageId)
                    .Select(s => s.StageOrder + 1).FirstOrDefault(),
                TotalStages = e.ApprovalRequest.ApprovalChain.Stages.Count,
                ExtendedData = e.ApprovalRequest.AssetApproval.ExtendedData
            });
    }

    public async Task<ApprovalRequestDetail?> GetRequestDetailAsync(int approvalRequestId)
    {
        var request = await _db.TblApprovalRequests
            .AsNoTracking()
            .Include(r => r.AssetApproval).ThenInclude(a => a.Asset)
            .Include(r => r.AssetApproval).ThenInclude(a => a.UploadedByUser)
            .Include(r => r.ApprovalChain).ThenInclude(c => c.Location)
            .Include(r => r.ApprovalChain).ThenInclude(c => c.Stages.OrderBy(s => s.StageOrder))
                .ThenInclude(s => s.StageGroups).ThenInclude(sg => sg.UserGroup)
            .Include(r => r.QueueEntries).ThenInclude(q => q.ResolvedByUser)
            .Include(r => r.Actions).ThenInclude(a => a.ActingUser)
            .FirstOrDefaultAsync(r => r.ApprovalRequestId == approvalRequestId);

        if (request == null) return null;

        return new ApprovalRequestDetail
        {
            ApprovalRequestId = request.ApprovalRequestId,
            AssetApprovalId = request.AssetApprovalId,
            Status = request.Status,
            SubmittedDate = request.SubmittedDate,
            CompletedDate = request.CompletedDate,
            UploadedByName = request.AssetApproval.UploadedByUser?.FriendlyName,
            AssetName = request.AssetApproval.Asset?.AssetName,
            LocationName = request.ApprovalChain?.Location?.LocationName,
            Stages = request.ApprovalChain?.Stages.OrderBy(s => s.StageOrder).Select(stage => new StageDetail
            {
                StageOrder = stage.StageOrder,
                StageName = stage.StageName,
                IntraStageOperator = stage.IntraStageOperator,
                IsCurrentStage = request.CurrentStageId == stage.ApprovalStageId,
                Groups = stage.StageGroups.OrderBy(sg => sg.SortOrder).Select(sg =>
                {
                    var queueEntry = request.QueueEntries.FirstOrDefault(q => q.ApprovalStageId == stage.ApprovalStageId && q.UserGroupId == sg.UserGroupId);
                    var action = request.Actions.FirstOrDefault(a => a.ApprovalStageId == stage.ApprovalStageId && a.UserGroupId == sg.UserGroupId);
                    return new GroupActionDetail
                    {
                        GroupId = sg.UserGroupId,
                        GroupName = sg.UserGroup?.GroupName ?? "Unknown",
                        QueueStatus = queueEntry?.Status ?? "not-reached",
                        ActingUserName = action?.ActingUser?.FriendlyName,
                        ActionType = action?.ActionType,
                        Notes = action?.Notes,
                        ActionDate = action?.ActionDate
                    };
                }).ToList()
            }).ToList() ?? new List<StageDetail>()
        };
    }

    #endregion

    #region Notifications

    private async Task NotifyGroupMembersAsync(IEnumerable<int> groupIds, string title, string message, int? referenceId, string? referenceUrl)
    {
        var userIds = await _db.TblUserGroupMemberships
            .AsNoTracking()
            .Where(m => groupIds.Contains(m.UserGroupId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();

        if (userIds.Count > 0)
        {
            await _notificationService.CreateBulkNotificationsAsync(
                userIds, title, message, "approval-pending", referenceId, referenceUrl);
        }
    }

    #endregion

    #region Comments

    public async Task AddCommentAsync(int assetApprovalId, int? approvalRequestId, int userId, string commentText, string commentType)
    {
        _db.TblApprovalComments.Add(new TblApprovalComment
        {
            AssetApprovalId = assetApprovalId,
            ApprovalRequestId = approvalRequestId,
            UserId = userId,
            CommentText = commentText,
            CommentType = commentType,
            CreatedDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<ApprovalCommentDto>> GetCommentsAsync(int assetApprovalId)
    {
        // Use a dedicated scope to avoid concurrent operation conflicts on the shared _db instance
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        return await db.TblApprovalComments
            .AsNoTracking()
            .Where(c => c.AssetApprovalId == assetApprovalId)
            .OrderBy(c => c.CreatedDate)
            .Select(c => new ApprovalCommentDto
            {
                ApprovalCommentId = c.ApprovalCommentId,
                UserName = c.User.FriendlyName ?? "Unknown",
                CommentText = c.CommentText,
                CommentType = c.CommentType,
                CreatedDate = c.CreatedDate
            })
            .ToListAsync();
    }

    #endregion

    #region Submissions

    public async Task<List<SubmissionItem>> GetSubmissionsForUserAsync(int userId)
    {
        return await _db.TblAssetApprovals
            .AsNoTracking()
            .Include(a => a.Asset)
                .ThenInclude(asset => asset.Location)
            .Where(a => a.UploadedByUserId == userId
                && a.ApprovalStatus != (int)DXSuite.Director.Models.Types.ApprovalStatus.Withdrawn)
            .OrderByDescending(a => a.SubmittedDate)
            .Select(a => new SubmissionItem
            {
                AssetApprovalId = a.AssetApprovalId,
                AssetId = a.AssetId,
                AssetName = a.Asset.AssetName,
                AssetPath = a.Asset.AssetPath,
                LocationName = a.Asset.Location.LocationName,
                ApprovalStatus = a.ApprovalStatus,
                SubmittedDate = a.SubmittedDate,
                ReviewedDate = a.ReviewedDate,
                DeclinedDate = a.DeclinedDate,
                ReviewedByUserId = a.ReviewedByUserId,
                ReviewNotes = a.ReviewNotes
            })
            .ToListAsync();
    }

    #endregion
}
