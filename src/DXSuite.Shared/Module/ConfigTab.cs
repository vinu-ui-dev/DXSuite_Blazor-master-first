namespace DXSuite.Shared.Module;

/// <summary>
/// Feature gates on the server-wide FeatureFlags options; TenantFeature gates on the
/// current tenant's TenantFeatureFlags (property name, resolved via reflection), letting
/// a tab be offered to some tenants on a shared deployment and hidden from the rest —
/// mirrors SettingsTab's Feature/TenantFeature split.
/// </summary>
public record ConfigTab(string Key, string Label, Type ComponentType, int Order, string? Feature = null, string? TenantFeature = null);
