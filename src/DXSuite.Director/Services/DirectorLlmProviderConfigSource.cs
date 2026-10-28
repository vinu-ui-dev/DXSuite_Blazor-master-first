using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Connectors.Services.Llm;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

/// <summary>
/// Director's implementation of ILlmProviderConfigSource.
/// Provides LLM provider configuration from TblLlmProviderConfiguration to the Connectors module's LlmGateway.
/// </summary>
public class DirectorLlmProviderConfigSource : ILlmProviderConfigSource
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DirectorLlmProviderConfigSource(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<LlmProviderConfig?> GetProviderConfigAsync(string? providerId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        TblLlmProviderConfiguration? config;

        if (string.IsNullOrEmpty(providerId))
        {
            // Get the default provider
            config = await db.TblLlmProviderConfigurations
                .AsNoTracking()
                .Where(c => c.IsEnabled && c.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);

            // Fall back to any enabled provider
            config ??= await db.TblLlmProviderConfigurations
                .AsNoTracking()
                .Where(c => c.IsEnabled)
                .OrderBy(c => c.ConfigId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            config = await db.TblLlmProviderConfigurations
                .AsNoTracking()
                .Where(c => c.ProviderId == providerId && c.IsEnabled)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (config == null) return null;

        return new LlmProviderConfig
        {
            ProviderId = config.ProviderId,
            ProviderType = config.ProviderType,
            BaseUrl = config.BaseUrl ?? string.Empty,
            ApiKey = config.ApiKey,
            Model = config.Model,
            MaxTokens = config.MaxTokens,
            DefaultTemperature = config.DefaultTemperature,
            TimeoutSeconds = config.TimeoutSeconds
        };
    }
}
