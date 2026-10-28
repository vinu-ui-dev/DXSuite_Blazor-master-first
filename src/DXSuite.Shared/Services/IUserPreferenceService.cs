namespace DXSuite.Shared.Services;

/// <summary>
/// User preference service interface for cross-module use (language selection, etc.).
/// Full implementation with configuration view settings lives in DXSuite.Director.
/// </summary>
public interface IUserPreferenceService
{
    Task<string?> GetPreferredLanguageAsync(int userId);
    Task SavePreferredLanguageAsync(int userId, string languageCode);

    Task<string?> GetDefaultLandingPageAsync(int userId);
    Task SaveDefaultLandingPageAsync(int userId, string? route);

    /// <summary>
    /// Whether this account renders without DXSuite's navigation chrome. See
    /// <see cref="KioskModeState"/> for how it combines with the per-request override.
    /// </summary>
    Task<bool> GetKioskModeAsync(int userId);
    Task SaveKioskModeAsync(int userId, bool kioskMode);
}
