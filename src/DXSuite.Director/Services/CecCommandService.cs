namespace DXSuite.Director.Services
{
    public interface ICecCommandService
    {
        /// <summary>
        /// Queues a CEC command for a specific device
        /// </summary>
        void QueueCommand(string deviceId, CecCommand command);
    }

    /// <summary>
    /// CEC (Consumer Electronics Control) command to be sent to a device
    /// </summary>
    public class CecCommand
    {
        /// <summary>
        /// The type of CEC command (power_on, power_off)
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Target output (e.g., "HDMI-1")
        /// </summary>
        public string? Output { get; set; }

        /// <summary>
        /// When the command was queued
        /// </summary>
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional user who requested the command
        /// </summary>
        public string? RequestedBy { get; set; }
    }

    /// <summary>
    /// Bridge for singleton callers (Sentinel drivers and command router) into the
    /// SQL-backed device command queue. Resolves a fresh scope per command, which binds
    /// to the default-tenant DbContext, matching the tenant behavior of the other
    /// background services. Request-scoped code should use IDeviceCommandService directly.
    /// </summary>
    public class CecCommandService : ICecCommandService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CecCommandService> _logger;

        public CecCommandService(IServiceScopeFactory scopeFactory, ILogger<CecCommandService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void QueueCommand(string deviceId, CecCommand command)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            // Fire-and-forget: callers are sync device drivers that must not block on DB I/O
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var commands = scope.ServiceProvider.GetRequiredService<IDeviceCommandService>();
                    await commands.QueueCecAsync(deviceId, command.Action, command.Output, command.RequestedBy);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to queue CEC command for device {DeviceId}", deviceId);
                }
            });
        }
    }
}
