using System.Text.Json.Serialization;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Configuration view settings stored in user's ExtendedData
/// </summary>
public class ConfigurationViewSettings
{
    [JsonPropertyName("deviceColumns")]
    public List<string>? DeviceColumnOrder { get; set; }

    [JsonPropertyName("locationColumns")]
    public List<string>? LocationColumnOrder { get; set; }

    /// <summary>Device grid column widths the user dragged, keyed by column id, as CSS lengths.</summary>
    [JsonPropertyName("deviceColumnWidths")]
    public Dictionary<string, string>? DeviceColumnWidths { get; set; }
}

/// <summary>
/// User extended data structure that includes configuration view settings
/// </summary>
public class UserExtendedData
{
    [JsonPropertyName("assignedHotelNumbers")]
    public List<string>? AssignedHotelNumbers { get; set; }

    [JsonPropertyName("configurationView")]
    public ConfigurationViewSettings? ConfigurationView { get; set; }

    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }

    [JsonPropertyName("defaultLandingPage")]
    public string? DefaultLandingPage { get; set; }

    /// <summary>
    /// Renders every page without DXSuite's navigation chrome for this account. Intended for
    /// dedicated display or panel accounts, which typically pair it with a DefaultLandingPage.
    /// Null rather than false when off, so the property stays absent for ordinary accounts.
    /// </summary>
    [JsonPropertyName("kioskMode")]
    public bool? KioskMode { get; set; }
}

/// <summary>
/// Extended user preference service with Director-specific configuration view settings.
/// </summary>
public interface IDirectorUserPreferenceService : DXSuite.Shared.Services.IUserPreferenceService
{
    Task<ConfigurationViewSettings?> GetConfigurationViewSettingsAsync(int userId);
    Task SaveConfigurationViewSettingsAsync(int userId, ConfigurationViewSettings settings);
    Task ClearConfigurationViewSettingsAsync(int userId);
}
