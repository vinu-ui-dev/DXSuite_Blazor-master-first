using DXSuite.Connectors.Services.CoreApi;

namespace DXSuite.Director.Services.CoreApi;

public static class DirectorCoreApiExtensions
{
    /// <summary>
    /// The tenant's CoreAPI connection and feed state, kept in its Director database and read by
    /// <see cref="CoreApiClient"/>, the watcher and the Flight Data Editor (Connectors and Aviation).
    /// </summary>
    public static IServiceCollection AddCoreApiStores(this IServiceCollection services)
    {
        services.AddScoped<ICoreApiSettingsStore, CoreApiSettingsStore>();
        services.AddScoped<CoreApiFeedStateStore>();
        services.AddScoped<ICoreApiFeedStateReader>(sp => sp.GetRequiredService<CoreApiFeedStateStore>());
        return services;
    }

    /// <summary>
    /// The watcher's switch-off (FDE-03), which reads <c>Aviation:FlightDataEditor:AllowOutageModeSwitch</c>
    /// through <see cref="DXSuite.Aviation.Services.FlightDataEditor.FlightDataEditorOptions"/>.
    /// </summary>
    public static IServiceCollection AddOutageSwitchOff(this IServiceCollection services)
    {
        services.AddScoped<OutageSwitchOff>();
        services.AddScoped<ICoreApiWatcherStartTask>(sp => sp.GetRequiredService<OutageSwitchOff>());
        services.AddScoped<ICoreApiWatcherSwitchTask>(sp => sp.GetRequiredService<OutageSwitchOff>());
        return services;
    }
}
