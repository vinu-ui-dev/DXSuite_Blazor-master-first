using DXSuite.Connectors.Services.CoreApi.Hub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.CoreApi;

public static class CoreApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CoreApiClient"/> as a typed client. The host must also register a scoped
    /// <see cref="ICoreApiSettingsStore"/> and <c>ISessionState</c>.
    /// </summary>
    public static IHttpClientBuilder AddCoreApiClient(this IServiceCollection services)
    {
        services.TryAddSingleton<CoreApiKeyProtector>();
        services.TryAddSingleton(TimeProvider.System);

        // No client timeout: the client limits each server attempt itself, so failover can move on.
        return services.AddHttpClient<CoreApiClient>(c => c.Timeout = Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Registers this instance's read-only hub listeners and <see cref="ICoreApiLiveFeed"/> for pages. The host
    /// must also register a scoped <see cref="ICoreApiSettingsStore"/> and <c>ITenantContext</c>.
    /// </summary>
    /// <param name="hubRetryDelay">How long a listener waits before reconnecting to a hub that is down.</param>
    public static IServiceCollection AddCoreApiLiveFeed(this IServiceCollection services, TimeSpan? hubRetryDelay = null)
    {
        services.TryAddSingleton(sp => new CoreApiHubListeners(
            sp.GetRequiredService<ILogger<CoreApiHubListeners>>(), hubRetryDelay ?? DefaultHubRetryDelay));
        services.TryAddScoped<ICoreApiLiveFeed, CoreApiLiveFeed>();
        return services;
    }

    /// <summary>How long a hub connection waits before reconnecting to a hub that is down.</summary>
    public static readonly TimeSpan DefaultHubRetryDelay = TimeSpan.FromSeconds(10);
}
