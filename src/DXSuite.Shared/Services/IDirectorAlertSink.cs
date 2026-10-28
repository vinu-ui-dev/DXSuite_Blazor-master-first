namespace DXSuite.Shared.Services;

public enum DirectorAlertKind
{
    UrlMissing,
    UrlRecovered,
    DeviceProbeFailed,
    DeviceProbeRecovered,
}

/// <summary>
/// A monitoring state transition (URL heartbeat lost/recovered, device probe
/// failed/recovered). Sinks receive one alert per transition — flap suppression
/// happens upstream in the monitoring service, so sinks may deliver every alert
/// they receive (e.g. straight into an email queue).
/// </summary>
public sealed record DirectorAlert(
    DirectorAlertKind Kind,
    /// <summary>Monitor id for URL alerts; connector/system name for device probes.</summary>
    string Source,
    string Message,
    DateTime OccurredUtc,
    string? Url = null,
    int? LocationId = null,
    /// <summary>Director tblDevice id when the source is linked to hardware.</summary>
    string? DeviceId = null,
    string? DeviceName = null);

/// <summary>
/// Delivery target for monitoring alerts. Implementations register with DI
/// (multiple allowed); the monitoring service fans each transition out to all
/// sinks and isolates their failures from each other.
/// </summary>
public interface IDirectorAlertSink
{
    Task PublishAsync(DirectorAlert alert, CancellationToken ct = default);
}
