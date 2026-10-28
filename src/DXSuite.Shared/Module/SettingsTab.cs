using DXSuite.Shared.Authorization;

namespace DXSuite.Shared.Module;

/// <summary>
/// Runtime visibility is permission-driven ("settings-tab:{Key}" grants managed in the
/// Roles settings tab). MinimumRole no longer gates visibility directly — it declares the
/// default grant applied to built-in roles when the tab key is first discovered:
/// lower RoleId = higher privilege (Admin=1, Architect=2, Contributor=3, NewUser=4), and a
/// tab with MinimumRole=Contributor is initially granted to Architect and Contributor
/// (Admin always has access). Default is Admin (no initial grants beyond Admin).
/// Feature gates on the server-wide FeatureFlags options; TenantFeature gates on the
/// current tenant's TenantFeatureFlags (property name, resolved via reflection), letting
/// a tab be offered to some tenants on a shared deployment and hidden from the rest.
/// RequiresPlatform hides the tab on deployments without the central Platform identity DB
/// (single-tenant/on-prem), whose services are only registered when its connection string
/// is configured.
/// </summary>
public record SettingsTab(string Key, string Label, Type ComponentType, int Order, Roles.Role MinimumRole = Roles.Role.Admin, string? Feature = null, string? Module = null, string? TenantFeature = null, bool RequiresPlatform = false);
