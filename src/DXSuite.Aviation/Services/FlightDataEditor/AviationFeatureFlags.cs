using DXSuite.Shared.Multitenancy;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>Tenant feature flag names Aviation pages and components gate on by name (reflection-matched
/// against <see cref="TenantFeatureFlags"/> by <see cref="RequiresTenantFeatureAttribute"/> and
/// <see cref="TenantFeatureFlags.IsEnabled"/>), kept as named constants so a typo fails at compile time.</summary>
public static class AviationFeatureFlags
{
    public const string FlightDataEditorFeature = nameof(TenantFeatureFlags.FlightDataEditor);
}
