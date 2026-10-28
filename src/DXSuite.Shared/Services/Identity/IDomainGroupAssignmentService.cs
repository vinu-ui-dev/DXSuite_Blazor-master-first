namespace DXSuite.Shared.Services.Identity;

public interface IDomainGroupAssignmentService
{
    Task SyncDomainGroupsAsync(int userId, string userPrincipalName);
}
