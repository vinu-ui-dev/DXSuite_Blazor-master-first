namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// Scoped service that performs a single evaluation cycle across all enabled devices.
/// Separated from the BackgroundService to enable scoped DI resolution and testability.
/// </summary>
public interface IDeviceEvaluator
{
    Task RunEvaluationCycleAsync(CancellationToken cancellationToken = default);
}
