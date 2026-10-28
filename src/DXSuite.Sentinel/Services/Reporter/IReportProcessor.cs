namespace DXSuite.Sentinel.Services.Reporter;

public interface IReportProcessor
{
    Task ProcessPendingEventsAsync(CancellationToken ct = default);
}
