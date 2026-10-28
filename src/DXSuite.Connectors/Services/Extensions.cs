using DXSuite.Connectors.Services.Connectors;
using DXSuite.Connectors.Services.Itsm;
using DXSuite.Connectors.Services.Llm;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Connectors.Services;

public static class Extensions
{
    public static IServiceCollection AddConnectorsModule(this IServiceCollection services, IConfiguration config)
    {
        // ──────────────────────────────────────────────────────────────────
        // LLM Providers
        // ──────────────────────────────────────────────────────────────────

        services.Configure<LlmOptions>(config.GetSection(LlmOptions.SectionName));

        services.AddSingleton<LlmApiKeyProtector>();

        services.AddHttpClient<OpenAiCompatibleProvider>();
        services.AddHttpClient<AnthropicProvider>();
        services.AddHttpClient<GeminiProvider>();
        services.AddHttpClient<OllamaProvider>();

        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<OpenAiCompatibleProvider>());
        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<GeminiProvider>());
        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<OllamaProvider>());

        services.AddSingleton<ILlmGateway, LlmGateway>();

        // ──────────────────────────────────────────────────────────────────
        // ITSM Adapters
        // ──────────────────────────────────────────────────────────────────

        // Default no-op provider — Sentinel module overrides this when enabled
        services.AddSingleton<IItsmConnectionProvider, NoOpItsmConnectionProvider>();

        services.AddSingleton<ItsmCredentialProtector>();

        services.AddHttpClient<JiraServiceMgmtAdapter>();
        services.AddHttpClient<RestGenericAdapter>();

        services.AddSingleton<IItsmAdapter>(sp => sp.GetRequiredService<JiraServiceMgmtAdapter>());
        services.AddSingleton<IItsmAdapter>(sp => sp.GetRequiredService<RestGenericAdapter>());

        services.AddSingleton<IItsmAdapterFactory, ItsmAdapterFactory>();

        services.AddHttpClient<JsmDiscoveryService>();
        services.AddSingleton<JsmDiscoveryService>();

        // ──────────────────────────────────────────────────────────────────
        // Cloud Platform Connectors
        // ──────────────────────────────────────────────────────────────────

        services.AddSingleton<ConnectorConfigProtector>();

        services.AddHttpClient<OmnivexInkConnector>();
        services.AddHttpClient<QSysReflectConnector>();

        services.AddSingleton<ICloudPlatformConnector>(sp => sp.GetRequiredService<OmnivexInkConnector>());
        services.AddSingleton<ICloudPlatformConnector>(sp => sp.GetRequiredService<QSysReflectConnector>());

        services.AddSingleton<ICloudPlatformConnectorRegistry, CloudPlatformConnectorRegistry>();

        return services;
    }
}
