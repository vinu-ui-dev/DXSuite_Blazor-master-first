using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Pulse.Services;

public static class Extensions
{
    public static IServiceCollection AddPulseModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IPulseService, PulseService>();
        return services;
    }
}
