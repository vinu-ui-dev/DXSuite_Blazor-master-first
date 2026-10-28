using DXSuite.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Shared.Services.Identity;

public static class IdentityServiceExtensions
{
    /// <summary>
    /// Registers identity services (user/group management, metadata, AD sync).
    /// Requires IIdentityDbContext to be registered separately — either via
    /// DirectorDbContext (full deploy) or IdentityDbContext (monitoring-only).
    /// </summary>
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddScoped<ISessionState, SessionState>();
        services.AddSingleton<IUserMetadataService, UserMetadataService>();
        services.AddSingleton<IUserGroupMetadataService, UserGroupMetadataService>();
        services.AddScoped<IUserGroupService, UserGroupService>();
        services.AddSingleton<RolePermissionCache>();
        services.AddScoped<IRolePermissionService, RolePermissionService>();
        services.AddScoped<IAdGroupSyncService, AdGroupSyncService>();
        services.AddSingleton<IDomainGroupAssignmentService, DomainGroupAssignmentService>();
        services.AddSingleton<IMetadataDefinitionService, MetadataDefinitionService>();
        services.AddScoped<IMetadataUsageContributor, IdentityMetadataUsageContributor>();
        services.AddSingleton<ILocalAccountService, LocalAccountService>();
        services.AddSingleton<ILdapCredentialService, LdapCredentialService>();
        services.AddSingleton<NotificationBroadcastService>();
        services.AddSingleton<LocationAccessBroadcastService>();
        // Scoped: chrome suppression is per circuit, and the query-string override has to survive
        // in-app navigation within that circuit.
        services.AddScoped<KioskModeState>();
        return services;
    }
}
