using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>A language a Message Type can be written in: a product catalogue code and its name.</summary>
public sealed record MessageLanguage(string Code, string Name);

/// <summary>
/// The languages Message Types are offered, from the product language catalogue (MSG-03). Messaging never asks
/// CoreAPI for languages (CAPI-09). Under D-02 only English and French are offered (DEV-11).
/// </summary>
public interface IMessageLanguageCatalogue
{
    /// <summary>True while D-02 fixes every type's languages to English and French.</summary>
    bool LanguagesFixed { get; }

    /// <summary>The languages offered, by name. Empty when the catalogue cannot be read.</summary>
    Task<IReadOnlyList<MessageLanguage>> OfferedAsync();

    /// <summary>Names <paramref name="codes"/> in the order given, falling back to the code itself.</summary>
    Task<IReadOnlyList<MessageLanguage>> NameAsync(IEnumerable<string> codes);
}

/// <inheritdoc cref="IMessageLanguageCatalogue"/>
public sealed class MessageLanguageCatalogue : IMessageLanguageCatalogue
{
    /// <summary>The fixed languages under D-02, in the order Ink shows them.</summary>
    public static readonly IReadOnlyList<MessageLanguage> GtaaLanguages =
        [new("eng", "English"), new("fra", "French")];

    private readonly bool _gtaa;
    private readonly ILanguageProvider? _provider;
    private readonly ILogger _logger;
    private IReadOnlyList<MessageLanguage>? _loaded;

    // The catalogue lives in CoreData, a module a host may leave out, so the provider is optional.
    public MessageLanguageCatalogue(IOptions<D02GtaaMessagingOptions> d02, ILanguageProvider? provider = null,
        ILogger<MessageLanguageCatalogue>? logger = null)
    {
        _gtaa = d02.Value.Enabled;
        _provider = provider;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public bool LanguagesFixed => _gtaa;

    public async Task<IReadOnlyList<MessageLanguage>> OfferedAsync()
    {
        if (_gtaa) return GtaaLanguages;
        if (_loaded is not null) return _loaded;
        if (_provider is null) return _loaded = [];

        try
        {
            _loaded = (await _provider.GetLanguagesAsync())
                .Where(l => l.IsActive && !string.IsNullOrWhiteSpace(l.LanguageCode))
                .Select(l => new MessageLanguage(l.LanguageCode.Trim(), string.IsNullOrWhiteSpace(l.LanguageName) ? l.LanguageCode.Trim() : l.LanguageName.Trim()))
                .DistinctBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A tenant without CoreData, or an unreachable CoreData database, still lets admins see and keep the
            // languages a type already has; only adding a new one waits on the catalogue.
            _logger.LogWarning(ex, "The product language catalogue could not be read for Message Types");
            _loaded = [];
        }
        return _loaded;
    }

    public async Task<IReadOnlyList<MessageLanguage>> NameAsync(IEnumerable<string> codes)
    {
        var offered = await OfferedAsync();
        return codes.Select(c => offered.FirstOrDefault(l => string.Equals(l.Code, c, StringComparison.OrdinalIgnoreCase))
                ?? GtaaLanguages.FirstOrDefault(l => string.Equals(l.Code, c, StringComparison.OrdinalIgnoreCase))
                ?? new MessageLanguage(c, c))
            .ToList();
    }
}
