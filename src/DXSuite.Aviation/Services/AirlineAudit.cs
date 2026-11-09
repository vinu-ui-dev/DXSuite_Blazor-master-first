using DXSuite.Shared.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DXSuite.Aviation.Services;

/// <summary>
/// Writes the Airlines tab's <c>airline.*</c> User Events for the signed-in account: created, onboarded, and
/// removed from the active list.
/// </summary>
/// <remarks>
/// Resolved like <see cref="FlightDataEditor.FlightEditAudit"/>: optional, so a host without the User Event
/// Log (no Director module) still runs Airlines config.
/// </remarks>
public sealed class AirlineAudit(IServiceProvider services, ILogger<AirlineAudit> logger)
{
    private readonly IUserEventLog? log = services.GetService<IUserEventLog>();

    public Task CreatedAsync(int recordId, string code, string name, object? details) =>
        WriteAsync(UserEventActionKeys.AirlineCreated, recordId, code, name, details);

    public Task OnboardedAsync(int recordId, string code, string name) =>
        WriteAsync(UserEventActionKeys.AirlineOnboarded, recordId, code, name, null);

    public Task RemovedFromActiveListAsync(int recordId, string code, string name) =>
        WriteAsync(UserEventActionKeys.AirlineRemovedFromActiveList, recordId, code, name, null);

    private Task WriteAsync(string actionKey, int recordId, string code, string name, object? details)
    {
        var target = new UserEventTarget(UserEventTargetTypes.Airline, recordId, $"{name} ({code})");
        var evt = new UserEvent(actionKey, target, Details: details);
        return log.TryWriteAsync(logger, actionKey, (l, ct) => l.WriteAsync(evt, ct));
    }
}

public static class AirlineAuditExtensions
{
    public static IServiceCollection AddAirlineAudit(this IServiceCollection services)
    {
        services.TryAddScoped<AirlineAudit>();
        return services;
    }
}
