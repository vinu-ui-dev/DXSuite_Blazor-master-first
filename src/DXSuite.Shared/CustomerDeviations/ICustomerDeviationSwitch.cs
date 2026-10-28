using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Shared.CustomerDeviations;

/// <summary>
/// Marker for a per-customer deviation switch (a documented, deliberate difference from the default
/// product behaviour for one customer's build — see <see cref="D02GtaaMessagingOptions"/> for an
/// example) bound from config section <c>CustomerDeviations:{Name}</c> and read through
/// <c>IOptions&lt;T&gt;</c>.
/// </summary>
public interface ICustomerDeviationSwitch
{
    /// <summary>The deviation's register row id, and its config section name under <c>CustomerDeviations</c>.</summary>
    static abstract string Name { get; }
}

public static class CustomerDeviationExtensions
{
    /// <summary>Binds <typeparamref name="T"/> from <c>CustomerDeviations:{T.Name}</c>; off (all defaults) when absent.</summary>
    public static IServiceCollection AddCustomerDeviation<T>(this IServiceCollection services, IConfiguration config)
        where T : class, ICustomerDeviationSwitch
    {
        services.Configure<T>(config.GetSection($"CustomerDeviations:{T.Name}"));
        return services;
    }
}
