namespace DXSuite.Director.Services.PtzCameras;

public class PtzCameraFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PtzCameraFactory> _logger;

    public PtzCameraFactory(IServiceProvider serviceProvider, ILogger<PtzCameraFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Creates appropriate camera client based on manufacturer name
    /// </summary>
    public IPtzCameraClient CreateClient(string manufacturer)
    {
        var normalizedManufacturer = manufacturer.ToLower().Trim();

        _logger.LogDebug("Creating camera client for manufacturer: {Manufacturer}", manufacturer);

        return normalizedManufacturer switch
        {
            "aver" => _serviceProvider.GetRequiredService<AverCameraClient>(),
            // Future camera support can be added here:
            // "sony" => _serviceProvider.GetRequiredService<SonyCameraClient>(),
            // "panasonic" => _serviceProvider.GetRequiredService<PanasonicCameraClient>(),
            _ => throw new NotSupportedException($"Camera manufacturer '{manufacturer}' is not supported. Supported manufacturers: Aver")
        };
    }
}
