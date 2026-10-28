using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DXSuite.Shared.Services.Identity;

public class DomainGroupAssignmentService : IDomainGroupAssignmentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainGroupAssignmentService> _logger;

    private const string EmailDomainKey = "EmailDomain";

    public DomainGroupAssignmentService(IServiceScopeFactory scopeFactory, ILogger<DomainGroupAssignmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task SyncDomainGroupsAsync(int userId, string userPrincipalName)
    {
        if (string.IsNullOrEmpty(userPrincipalName) || !userPrincipalName.Contains('@'))
            return;

        var domain = userPrincipalName.Split('@')[1].ToLowerInvariant();

        using var scope = _scopeFactory.CreateScope();
        var groupService = scope.ServiceProvider.GetRequiredService<IUserGroupService>();
        var metadataService = scope.ServiceProvider.GetRequiredService<IUserGroupMetadataService>();

        var allGroups = await groupService.GetAllGroupsAsync("director");
        if (allGroups == null || allGroups.Count == 0) return;

        foreach (var group in allGroups)
        {
            try
            {
                var metadata = await metadataService.GetMetadataAsync(group.UserGroupId);
                if (!metadata.TryGetValue(EmailDomainKey, out var domainValues))
                    continue;

                // One domain per value; a value may also hold comma-separated domains ("delta.com,acme.org")
                var domains = domainValues
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Select(d => d.ToLowerInvariant());

                if (domains.Contains(domain))
                {
                    await groupService.AddMemberAsync(group.UserGroupId, userId);
                    _logger.LogDebug("Auto-joined user {UserId} to group {GroupId} via domain match ({Domain})",
                        userId, group.UserGroupId, domain);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process domain group {GroupId} for user {UserId}", group.UserGroupId, userId);
            }
        }
    }
}
