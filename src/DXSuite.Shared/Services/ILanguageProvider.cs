namespace DXSuite.Shared.Services;

/// <summary>
/// Cross-module language/locale lookup.
/// Implemented by CoreData, consumed by Director and other modules.
/// </summary>
public interface ILanguageProvider
{
    Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync();
    Task<IReadOnlyList<CountryLanguageInfo>> GetCountryLanguagesAsync();
}

public record LanguageInfo(int LanguageId, string LanguageCode, string LanguageName, bool IsActive);
public record CountryLanguageInfo(int CountryLanguageId, string CountryCode, string LanguageCode, string CountryName);
