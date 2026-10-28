using DXSuite.Shared.Authorization;

namespace DXSuite.Shared.Module;

/// <summary>
/// Feature names a TenantFeatureFlags property that must be true for the item to render.
/// Permission is a role-permission key (see PermissionKeys) the user's role must hold —
/// the real Admin role bypasses permission checks. IsVisible covers gates outside the
/// role-permission system (e.g. platform identity flags) and is evaluated against the
/// current session after it has loaded.
/// </summary>
public record NavItem(string Label, string Href, string Icon, int Order, string? Group = null, string? Feature = null, string? Permission = null, Func<ISessionState, bool>? IsVisible = null);
